using System.Threading.Channels;
using HiveShock.Logging;

namespace HiveShock.Input;

/// <summary>
/// Ejecuta macros de teclas de una en una (un solo lector, como <c>EffectDispatcher</c>). Garantiza que ninguna
/// tecla se quede pulsada: al terminar, cancelar o fallar suelta todo lo que dejó en Down.
/// </summary>
public sealed class KeyMacroRunner : IDisposable
{
    /// <summary>Margen para no devolver el foco entre dos macros seguidas.</summary>
    public static readonly TimeSpan FocusDebounce = TimeSpan.FromMilliseconds(400);

    private readonly Channel<Work> _channel = Channel.CreateUnbounded<Work>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    private readonly IGameInput _input;
    private readonly Func<WindowTarget?> _target;
    private readonly Func<bool> _isDryRun;
    private readonly Func<int, CancellationToken, Task> _delay;
    private readonly Func<double> _random;
    private readonly object _gate = new();
    private readonly Dictionary<string, int> _active = new(StringComparer.OrdinalIgnoreCase);
    private int _generation;
    private CancellationTokenSource? _current;
    private string? _currentId;
    private DateTime _lastDeniedLog;

    public KeyMacroRunner(
        IGameInput input,
        Func<WindowTarget?> target,
        Func<bool>? isDryRun = null,
        Func<int, CancellationToken, Task>? delay = null,
        Func<double>? random = null)
    {
        _input = input;
        _target = target;
        _isDryRun = isDryRun ?? (() => false);
        _delay = delay ?? ((ms, ct) => Task.Delay(ms, ct));
        _random = random ?? Random.Shared.NextDouble;
    }

    public void Dispose() => CancelAll();

    /// <summary>Encola la macro respetando su política de solape. Devuelve false si se descartó.</summary>
    public bool Enqueue(KeyMacro macro, string? user, string reason)
    {
        var errors = KeyMacroValidator.Validate(macro);
        if (errors.Count > 0)
        {
            BridgeLog.Warn($"Macro de teclas «{macro.Id}» rechazada: {errors[0]}");
            return false;
        }

        lock (_gate)
        {
            var busy = _active.TryGetValue(macro.Id, out var n) && n > 0;
            if (busy && macro.Overlap == MacroOverlap.Drop)
            {
                BridgeLog.Info($"Descartado {macro.Id} <- {user ?? "anon"} ({reason}): ya se está ejecutando");
                return false;
            }

            if (busy && macro.Overlap == MacroOverlap.Replace && string.Equals(_currentId, macro.Id, StringComparison.OrdinalIgnoreCase))
            {
                _current?.Cancel();
            }

            _active[macro.Id] = n + 1;
        }

        if (!_channel.Writer.TryWrite(new Work(macro, user, reason, Volatile.Read(ref _generation))))
        {
            Release(macro.Id);
            return false;
        }

        return true;
    }

    /// <summary>Pausa de emergencia: lo pendiente se descarta y la macro en curso se corta (suelta teclas).</summary>
    public void CancelAll()
    {
        Interlocked.Increment(ref _generation);
        lock (_gate)
        {
            _current?.Cancel();
        }
    }

    public async Task RunAsync(CancellationToken ct)
    {
        IInputSession? session = null;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (!_channel.Reader.TryRead(out var item))
                {
                    // Cola vacía: si hay sesión abierta espera un instante por si llega otra macro antes de devolver el foco.
                    if (session != null)
                    {
                        using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        wait.CancelAfter(FocusDebounce);
                        try
                        {
                            if (!await _channel.Reader.WaitToReadAsync(wait.Token).ConfigureAwait(false))
                            {
                                break;
                            }

                            continue;
                        }
                        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                        {
                            session.Dispose();
                            session = null;
                            continue;
                        }
                    }

                    if (!await _channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
                    {
                        break;
                    }

                    continue;
                }

                try
                {
                    session = await RunItemAsync(item, session, ct).ConfigureAwait(false);
                }
                finally
                {
                    Release(item.Macro.Id);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Apagado a propósito.
        }
        finally
        {
            session?.Dispose();
        }
    }

    private async Task<IInputSession?> RunItemAsync(Work item, IInputSession? session, CancellationToken ct)
    {
        var macro = item.Macro;
        if (item.Generation != Volatile.Read(ref _generation))
        {
            BridgeLog.Info($"Descartado {macro.Id} <- {item.User ?? "anon"} ({item.Reason}): efectos en pausa");
            return session;
        }

        if (_isDryRun())
        {
            BridgeLog.Info($"[prueba] {macro.Id} <- {item.User ?? "anon"} ({item.Reason}): {macro.Steps.Count} paso(s), ~{KeyMacroValidator.EstimateTotalMs(macro)} ms");
            return session;
        }

        var target = _target();
        if (target is null || target.IsEmpty)
        {
            NoteDenied("No hay ventana del juego configurada para enviar teclas.");
            return session;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lock (_gate)
        {
            _current = cts;
            _currentId = macro.Id;
        }

        var held = new HashSet<KeyCode>();
        try
        {
            if (session is null)
            {
                var status = _input.GetStatus(target);
                if (status is WindowStatus.NotFound or WindowStatus.Elevated or WindowStatus.Unsupported)
                {
                    NoteDenied(status switch
                    {
                        WindowStatus.Elevated => "El juego corre como administrador; ejecuta HiveShock como administrador para enviarle teclas.",
                        WindowStatus.NotFound => "No se encuentra la ventana del juego.",
                        _ => "Este sistema no soporta enviar teclas.",
                    });
                    return null;
                }

                session = await _input.BeginAsync(target, cts.Token).ConfigureAwait(false);
                if (session is null)
                {
                    NoteDenied("No se pudo poner el juego en primer plano.");
                    return null;
                }
            }

            var started = Environment.TickCount64;
            foreach (var step in macro.Steps)
            {
                GameKeys.TryGet(step.Key, out var key);
                for (var r = 0; r < step.Repeat; r++)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    if (Environment.TickCount64 - started > macro.MaxTotalMs)
                    {
                        BridgeLog.Warn($"Macro «{macro.Id}» cortada: superó {macro.MaxTotalMs} ms.");
                        return session;
                    }

                    switch (step.Action)
                    {
                        case KeyAction.Tap:
                            Press(session, held, key);
                            await _delay(Jitter(KeyMacroValidator.TapMs, step.JitterPercent), cts.Token).ConfigureAwait(false);
                            Unpress(session, held, key);
                            break;
                        case KeyAction.Hold:
                            Press(session, held, key);
                            await _delay(Jitter(step.HoldMs, step.JitterPercent), cts.Token).ConfigureAwait(false);
                            Unpress(session, held, key);
                            break;
                        case KeyAction.Down:
                            Press(session, held, key);
                            break;
                        case KeyAction.Up:
                            Unpress(session, held, key);
                            break;
                    }

                    if (step.DelayAfterMs > 0)
                    {
                        await _delay(Jitter(step.DelayAfterMs, step.JitterPercent), cts.Token).ConfigureAwait(false);
                    }
                }
            }

            BridgeLog.Info($"OK {macro.Id} <- {item.User ?? "anon"} ({item.Reason})");
            return session;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            BridgeLog.Info($"Macro «{macro.Id}» cancelada");
            return session;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            BridgeLog.Error($"Fallo macro {macro.Id}: {ex.Message}");
            return session;
        }
        finally
        {
            if (session != null)
            {
                foreach (var key in held)
                {
                    try
                    {
                        session.Send(key, false);
                    }
                    catch
                    {
                        // Soltar es lo último que se intenta; no debe tapar el error original.
                    }
                }
            }

            lock (_gate)
            {
                _current = null;
                _currentId = null;
            }
        }
    }

    private static void Press(IInputSession session, HashSet<KeyCode> held, KeyCode key)
    {
        session.Send(key, true);
        held.Add(key);
    }

    private static void Unpress(IInputSession session, HashSet<KeyCode> held, KeyCode key)
    {
        session.Send(key, false);
        held.Remove(key);
    }

    private int Jitter(int ms, int percent)
    {
        if (percent <= 0 || ms <= 0)
        {
            return ms;
        }

        var factor = 1 + ((_random() * 2) - 1) * percent / 100.0;
        return Math.Max(1, (int)Math.Round(ms * factor));
    }

    private void Release(string id)
    {
        lock (_gate)
        {
            if (_active.TryGetValue(id, out var n))
            {
                _active[id] = Math.Max(0, n - 1);
            }
        }
    }

    /// <summary>Un aviso por ventana de 10 s: un chat entero no debe inundar el log con el mismo fallo.</summary>
    private void NoteDenied(string message)
    {
        var now = DateTime.UtcNow;
        if (now - _lastDeniedLog < TimeSpan.FromSeconds(10))
        {
            return;
        }

        _lastDeniedLog = now;
        BridgeLog.Warn(message);
    }

    private sealed record Work(KeyMacro Macro, string? User, string Reason, int Generation);
}
