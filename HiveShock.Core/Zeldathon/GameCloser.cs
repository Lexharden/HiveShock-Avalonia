using System.Diagnostics;
using HiveShock.Logging;

namespace HiveShock.Zeldathon;

/// <summary>Procesos del sistema. Tras una interfaz para probar el cierre sin matar nada de verdad.</summary>
public interface IProcessControl
{
    IReadOnlyList<int> Find(IEnumerable<string> names);

    bool IsAlive(int pid);

    void Kill(int pid);
}

public sealed class SystemProcessControl : IProcessControl
{
    public IReadOnlyList<int> Find(IEnumerable<string> names)
    {
        var ids = new List<int>();
        foreach (var name in names.Select(n => Path.GetFileNameWithoutExtension(n.Trim())).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                ids.Add(p.Id);
                p.Dispose();
            }
        }

        return ids;
    }

    public bool IsAlive(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public void Kill(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            p.Kill();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // ya se cerró, o no hay permiso: se avisa al usuario más arriba
        }
    }
}

public enum GameCloseOutcome
{
    /// <summary>El juego se cerró solo tras pedírselo.</summary>
    Graceful,

    /// <summary>Hubo que terminar el proceso.</summary>
    Killed,

    /// <summary>El juego ya no estaba abierto.</summary>
    NotRunning,

    /// <summary>No se sabe cómo se llama el proceso del juego: solo se pudo pedir el cierre.</summary>
    Unknown,

    /// <summary>Se intentó terminar el proceso pero sigue abierto.</summary>
    Failed,
}

/// <summary>
/// Cierra el juego cuando el servidor lo ordena (se acabó el tiempo del día): primero se lo pide al
/// juego (cierre limpio, guarda la partida) y, si sigue abierto pasado el plazo, termina el proceso.
/// </summary>
public sealed class GameCloser
{
    private readonly Func<CancellationToken, Task> _requestQuit;
    private readonly Func<IReadOnlyCollection<string>> _processNames;
    private readonly IProcessControl _processes;
    private readonly TimeSpan _grace;
    private readonly TimeSpan _poll;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private int _running;

    public GameCloser(
        Func<CancellationToken, Task> requestQuit,
        Func<IReadOnlyCollection<string>> processNames,
        IProcessControl? processes = null,
        TimeSpan? grace = null,
        TimeSpan? poll = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _requestQuit = requestQuit;
        _processNames = processNames;
        _processes = processes ?? new SystemProcessControl();
        _grace = grace ?? TimeSpan.FromSeconds(10);
        _poll = poll ?? TimeSpan.FromMilliseconds(500);
        _delay = delay ?? Task.Delay;
    }

    /// <summary>Se dispara con un mensaje listo para mostrar al usuario.</summary>
    public event Action<GameCloseOutcome, string>? Finished;

    public bool IsClosing => Volatile.Read(ref _running) == 1;

    public async Task<GameCloseOutcome?> CloseAsync(CancellationToken ct = default)
    {
        // Una orden repetida del servidor mientras ya se está cerrando no lanza otro cierre.
        if (Interlocked.Exchange(ref _running, 1) == 1)
        {
            return null;
        }

        try
        {
            var names = _processNames();
            var before = names.Count > 0 ? _processes.Find(names) : [];
            if (names.Count > 0 && before.Count == 0)
            {
                return Report(GameCloseOutcome.NotRunning, "El tiempo de hoy se agotó; el juego ya estaba cerrado.");
            }

            try
            {
                await _requestQuit(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                BridgeLog.Warn($"Zeldatón: no se pudo pedir el cierre al juego ({ex.Message})");
            }

            if (names.Count == 0)
            {
                return Report(GameCloseOutcome.Unknown, "El tiempo de hoy se agotó. Se pidió cerrar el juego; ciérralo tú si sigue abierto.");
            }

            var waited = TimeSpan.Zero;
            while (waited < _grace)
            {
                if (!before.Any(_processes.IsAlive))
                {
                    return Report(GameCloseOutcome.Graceful, "El tiempo de hoy se agotó: el juego se cerró.");
                }

                await _delay(_poll, ct).ConfigureAwait(false);
                waited += _poll;
            }

            foreach (var pid in _processes.Find(names))
            {
                _processes.Kill(pid);
            }

            await _delay(_poll, ct).ConfigureAwait(false);
            return _processes.Find(names).Count == 0
                ? Report(GameCloseOutcome.Killed, "El tiempo de hoy se agotó: se cerró el juego a la fuerza.")
                : Report(GameCloseOutcome.Failed, "El tiempo de hoy se agotó pero no se pudo cerrar el juego. Ciérralo tú ahora.");
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    private GameCloseOutcome Report(GameCloseOutcome outcome, string message)
    {
        BridgeLog.Warn($"Zeldatón: {message}");
        Finished?.Invoke(outcome, message);
        return outcome;
    }
}
