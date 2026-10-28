using HiveShock.Configuration;
using HiveShock.Logging;
using HiveShock.Platform;

namespace HiveShock.Hosting;

/// <summary>Ids de los efectos del sistema (no son del juego ni macros de teclas).</summary>
public static class SystemEffectIds
{
    public const string RestartPc = "reiniciar_pc";

    public const string RestartPcLabel = "Reiniciar el equipo (con cuenta atrás)";

    public static bool IsSystemEffect(string? effectId) =>
        string.Equals(effectId, RestartPc, StringComparison.OrdinalIgnoreCase);

    /// <summary>Los que este sistema puede ejecutar: hoy solo Windows.</summary>
    public static IReadOnlyList<string> Supported(IPowerControl power) =>
        power.IsSupported ? [RestartPc] : [];
}

/// <summary>Ajustes del efecto de reinicio. El «armado» NO se guarda: cada sesión empieza desarmada.</summary>
public sealed class SystemEffectSettings
{
    public const int MinCountdownSec = 10;
    public const int MaxCountdownSec = 300;
    public const int DefaultCountdownSec = 30;

    public int CountdownSec { get; set; } = DefaultCountdownSec;

    /// <summary>Cierra los programas sin preguntar. Se pierde lo que no esté guardado.</summary>
    public bool Force { get; set; }

    public int ClampedCountdown => Math.Clamp(CountdownSec, MinCountdownSec, MaxCountdownSec);
}

/// <summary>
/// Reinicio del equipo disparado como un efecto más (regalo, comando…). Está pensado para no sorprender:
/// desarmado por defecto en cada sesión, con cuenta atrás visible y cancelable, una sola vez por sesión,
/// y sin ejecutarse en «Solo simular» ni con la pausa de emergencia puesta.
/// </summary>
public sealed class SystemEffectService : IDisposable
{
    public const string SettingsFile = ".hiveshock-system.json";

    /// <summary>Atajo global que cancela la cuenta atrás aunque estés dentro del juego. Solo existe mientras cuenta.</summary>
    public const string CancelHotkey = "Ctrl+Alt+Shift+F12";

    private readonly object _gate = new();
    private readonly IPowerControl _power;
    private readonly Func<bool> _isDryRun;
    private readonly Func<bool> _isPaused;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private CancellationTokenSource? _cts;

    public SystemEffectService(
        IPowerControl power,
        Func<bool> isDryRun,
        Func<bool> isPaused,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        SystemEffectSettings? settings = null)
    {
        _power = power;
        _isDryRun = isDryRun;
        _isPaused = isPaused;
        _delay = delay ?? Task.Delay;
        Settings = settings ?? UserDataStore.Load<SystemEffectSettings>(SettingsFile) ?? new SystemEffectSettings();
    }

    public SystemEffectSettings Settings { get; }

    public bool IsSupported => _power.IsSupported;

    /// <summary>Solo vale esta sesión; al reabrir HiveShock vuelve a estar desarmado.</summary>
    public bool Armed { get; private set; }

    public bool IsCountingDown { get; private set; }

    public int SecondsLeft { get; private set; }

    public string? TriggeredBy { get; private set; }

    /// <summary>Ya se ejecutó (o se está ejecutando) un reinicio en esta sesión.</summary>
    public bool UsedThisSession { get; private set; }

    /// <summary>Cambió el estado o pasó un segundo de la cuenta atrás. Llega desde un hilo de fondo.</summary>
    public event Action? Changed;

    public void SetArmed(bool armed)
    {
        lock (_gate)
        {
            if (Armed == armed)
            {
                return;
            }

            Armed = armed;
        }

        BridgeLog.Info(armed ? "Reinicio del equipo: ARMADO para esta sesión." : "Reinicio del equipo: desarmado.");
        if (!armed)
        {
            Cancel("desarmado");
        }

        Changed?.Invoke();
    }

    public void Configure(int countdownSec, bool force)
    {
        Settings.CountdownSec = Math.Clamp(countdownSec, SystemEffectSettings.MinCountdownSec, SystemEffectSettings.MaxCountdownSec);
        Settings.Force = force;
        UserDataStore.Save(SettingsFile, Settings);
        Changed?.Invoke();
    }

    /// <summary>
    /// Inicia la cuenta atrás si procede. <paramref name="preview"/> hace todo menos reiniciar (para probarla).
    /// False si se ignoró; el motivo queda en el log.
    /// </summary>
    public bool Trigger(string effectId, string? user, string reason, bool preview = false)
    {
        if (!SystemEffectIds.IsSystemEffect(effectId))
        {
            return false;
        }

        var who = string.IsNullOrWhiteSpace(user) ? "alguien" : user;
        string? ignored = null;
        CancellationToken token;
        lock (_gate)
        {
            if (!_power.IsSupported)
            {
                ignored = "este sistema no puede reiniciarse desde HiveShock";
            }
            else if (!preview && !Armed)
            {
                ignored = "no está armado";
            }
            else if (!preview && _isPaused())
            {
                ignored = "los efectos están en pausa";
            }
            else if (IsCountingDown)
            {
                ignored = "ya hay una cuenta atrás en marcha";
            }
            else if (!preview && UsedThisSession)
            {
                ignored = "ya se usó en esta sesión";
            }

            if (ignored != null)
            {
                token = CancellationToken.None;
            }
            else
            {
                _cts = new CancellationTokenSource();
                token = _cts.Token;
                IsCountingDown = true;
                SecondsLeft = Settings.ClampedCountdown;
                TriggeredBy = who;
            }
        }

        if (ignored != null)
        {
            BridgeLog.Warn($"Reinicio del equipo ignorado ({who}, {reason}): {ignored}.");
            return false;
        }

        var seconds = Settings.ClampedCountdown;
        var force = Settings.Force;
        BridgeLog.Warn(
            $"Reinicio del equipo en {seconds} s ({who}, {reason}){(force ? " FORZADO" : "")}{(preview ? " [prueba: no reiniciará]" : "")}. " +
            $"Cancela con {CancelHotkey} o desde HiveShock.");
        Changed?.Invoke();
        Task.Run(() => CountdownAsync(seconds, force, preview, token)).FireAndLog("Cuenta atrás");
        return true;
    }

    /// <summary>Cancela la cuenta atrás en marcha. True si había una.</summary>
    public bool Cancel(string why)
    {
        CancellationTokenSource? cts;
        lock (_gate)
        {
            if (!IsCountingDown)
            {
                return false;
            }

            cts = _cts;
        }

        BridgeLog.Warn($"Reinicio del equipo CANCELADO: {why}.");
        cts?.Cancel();
        return true;
    }

    public void Dispose()
    {
        _cts?.Cancel();
    }

    private async Task CountdownAsync(int seconds, bool force, bool preview, CancellationToken ct)
    {
        try
        {
            for (var left = seconds; left > 0; left--)
            {
                ct.ThrowIfCancellationRequested();
                if (!preview && _isPaused())
                {
                    Cancel("pausa de emergencia");
                    ct.ThrowIfCancellationRequested();
                }

                lock (_gate)
                {
                    SecondsLeft = left;
                }

                Changed?.Invoke();
                if (left is 10 or 5 or 3 or 2 or 1)
                {
                    BridgeLog.Warn($"Reinicio del equipo en {left} s…");
                }

                await _delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
            }

            ct.ThrowIfCancellationRequested();
            if (preview || _isDryRun())
            {
                BridgeLog.Info(preview
                    ? "Prueba de cuenta atrás terminada: aquí se habría reiniciado."
                    : "Dry-run: aquí se habría reiniciado el equipo.");
                return;
            }

            lock (_gate)
            {
                UsedThisSession = true;
            }

            BridgeLog.Warn($"Reiniciando el equipo ahora{(force ? " (forzado)" : "")}.");
            _power.Restart(force);
        }
        catch (OperationCanceledException)
        {
            // cancelado: ya se registró el motivo
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                UsedThisSession = false;
            }

            BridgeLog.Error($"No se pudo reiniciar el equipo: {ex.Message}");
        }
        finally
        {
            lock (_gate)
            {
                IsCountingDown = false;
                SecondsLeft = 0;
                TriggeredBy = null;
                _cts?.Dispose();
                _cts = null;
            }

            Changed?.Invoke();
        }
    }
}
