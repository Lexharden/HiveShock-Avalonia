using System.Diagnostics;

namespace HiveShock.Zeldathon;

public enum ZeldathonRacerStatus
{
    Unknown,
    Online,
    Live,
    Paused,
    Offline,
    Exhausted,
    Finished,
}

/// <summary>Reloj oficial tal como lo manda el servidor (<c>CLOCK</c>).</summary>
public sealed record ZeldathonClockSnapshot(
    string RacerId,
    long RemainingMs,
    ZeldathonRacerStatus Status,
    DateTime ResetAtUtc,
    DateTime ServerTimeUtc);

/// <summary>
/// Interpola el reloj oficial entre mensajes del servidor. El servidor es la autoridad: solo cuenta atrás
/// mientras el estado es <c>live</c>, y cada <c>CLOCK</c> nuevo corrige el valor. Misma regla que
/// <c>computeRemainingMs</c> de la web. Sin conexión, se congela en el momento de la caída (el servidor
/// también deja de contar cuando pierde el latido).
/// </summary>
public sealed class ZeldathonClock
{
    private readonly Func<long> _nowMs;
    private readonly object _gate = new();
    private ZeldathonClockSnapshot? _snapshot;
    private long _receivedAtMs;
    private long _rttMs;
    private long? _disconnectedAtMs;

    public ZeldathonClock(Func<long>? monotonicMs = null) =>
        _nowMs = monotonicMs ?? (() => Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency);

    public event Action? Changed;

    public bool HasValue
    {
        get
        {
            lock (_gate)
            {
                return _snapshot != null;
            }
        }
    }

    public ZeldathonClockSnapshot? Snapshot
    {
        get
        {
            lock (_gate)
            {
                return _snapshot;
            }
        }
    }

    public ZeldathonRacerStatus Status => Snapshot?.Status ?? ZeldathonRacerStatus.Unknown;

    /// <summary>True si se perdió la conexión y lo mostrado es el último valor conocido.</summary>
    public bool IsDisconnected
    {
        get
        {
            lock (_gate)
            {
                return _disconnectedAtMs != null;
            }
        }
    }

    /// <param name="rttMs">Ida y vuelta de la petición que trajo este reloj (0 si no se sabe, p. ej. un push).</param>
    public void Update(ZeldathonClockSnapshot snapshot, long rttMs = 0)
    {
        lock (_gate)
        {
            _snapshot = snapshot;
            _receivedAtMs = _nowMs();
            _rttMs = Math.Clamp(rttMs, 0, 10_000);
            _disconnectedAtMs = null;
        }

        Changed?.Invoke();
    }

    public void MarkDisconnected()
    {
        lock (_gate)
        {
            if (_snapshot == null || _disconnectedAtMs != null)
            {
                return;
            }

            _disconnectedAtMs = _nowMs();
        }

        Changed?.Invoke();
    }

    public void Clear()
    {
        lock (_gate)
        {
            _snapshot = null;
            _disconnectedAtMs = null;
        }

        Changed?.Invoke();
    }

    /// <summary>Tiempo restante oficial, en ms (0 si aún no hay dato).</summary>
    public long RemainingMs()
    {
        lock (_gate)
        {
            if (_snapshot == null)
            {
                return 0;
            }

            if (_snapshot.Status != ZeldathonRacerStatus.Live)
            {
                return Math.Max(0, _snapshot.RemainingMs);
            }

            var until = _disconnectedAtMs ?? _nowMs();
            var elapsed = Math.Max(0, until - _receivedAtMs) + _rttMs / 2;
            return Math.Max(0, _snapshot.RemainingMs - elapsed);
        }
    }

    /// <summary>Tiempo que falta para el reinicio diario, en ms (0 si no se sabe).</summary>
    public long UntilResetMs()
    {
        lock (_gate)
        {
            if (_snapshot == null)
            {
                return 0;
            }

            var elapsed = Math.Max(0, (_disconnectedAtMs ?? _nowMs()) - _receivedAtMs);
            var serverNow = _snapshot.ServerTimeUtc.AddMilliseconds(elapsed);
            return Math.Max(0, (long)(_snapshot.ResetAtUtc - serverNow).TotalMilliseconds);
        }
    }
}
