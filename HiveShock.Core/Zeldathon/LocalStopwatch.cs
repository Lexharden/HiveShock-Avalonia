using System.Diagnostics;

namespace HiveShock.Zeldathon;

/// <summary>
/// Cronómetro manual del overlay (modo local, sin servidor). Sube desde cero o baja desde un tiempo
/// dado. Usa reloj monótono, así un cambio de hora del sistema no lo altera.
/// </summary>
public sealed class LocalStopwatch
{
    private readonly Func<long> _nowMs;
    private long _baseMs;
    private long _startedAtMs;
    private bool _running;

    public LocalStopwatch(long elapsedMs = 0, Func<long>? monotonicMs = null)
    {
        _nowMs = monotonicMs ?? (() => Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency);
        _baseMs = Math.Max(0, elapsedMs);
    }

    public bool IsRunning => _running;

    public long ElapsedMs => _running ? _baseMs + Math.Max(0, _nowMs() - _startedAtMs) : _baseMs;

    public void Start()
    {
        if (_running)
        {
            return;
        }

        _startedAtMs = _nowMs();
        _running = true;
    }

    public void Pause()
    {
        if (!_running)
        {
            return;
        }

        _baseMs = ElapsedMs;
        _running = false;
    }

    public void Reset()
    {
        _baseMs = 0;
        _running = false;
    }
}
