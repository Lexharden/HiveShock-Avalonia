using System.Diagnostics;

namespace HiveShock.Zeldathon;

/// <summary>
/// Cronómetro manual del overlay (modo local, sin servidor). Sube desde cero o baja desde un tiempo
/// dado. Usa reloj monótono, así un cambio de hora del sistema no lo altera. Las donaciones lo ajustan
/// con <see cref="Adjust"/>: el ajuste va aparte del tiempo transcurrido, así pausar, reanudar o poner a
/// cero siguen funcionando igual. Se puede tocar desde varios hilos (la interfaz y los canales del live).
/// </summary>
public sealed class LocalStopwatch
{
    private readonly object _gate = new();
    private readonly Func<long> _nowMs;
    private long _baseMs;
    private long _offsetMs;
    private long _startedAtMs;
    private bool _running;

    public LocalStopwatch(long elapsedMs = 0, Func<long>? monotonicMs = null, long offsetMs = 0)
    {
        _nowMs = monotonicMs ?? (() => Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency);
        _baseMs = Math.Max(0, elapsedMs);
        _offsetMs = offsetMs;
    }

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _running;
            }
        }
    }

    public long ElapsedMs
    {
        get
        {
            lock (_gate)
            {
                return ElapsedUnlocked();
            }
        }
    }

    /// <summary>
    /// Lo que las donaciones han sumado (+) o restado (−) al tiempo mostrado. Con cronómetro hacia arriba
    /// se suma al número; con cuenta atrás se suma al tiempo que queda.
    /// </summary>
    public long OffsetMs
    {
        get
        {
            lock (_gate)
            {
                return _offsetMs;
            }
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_running)
            {
                return;
            }

            _startedAtMs = _nowMs();
            _running = true;
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (!_running)
            {
                return;
            }

            _baseMs = ElapsedUnlocked();
            _running = false;
        }
    }

    /// <summary>Vuelve a cero y borra también lo que sumaron o restaron las donaciones.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _baseMs = 0;
            _offsetMs = 0;
            _running = false;
        }
    }

    /// <summary>Suma (o resta, si es negativo) tiempo al que muestra el cronómetro, esté corriendo o en pausa.</summary>
    public void Adjust(long deltaMs)
    {
        lock (_gate)
        {
            _offsetMs += deltaMs;
        }
    }

    private long ElapsedUnlocked() => _running ? _baseMs + Math.Max(0, _nowMs() - _startedAtMs) : _baseMs;
}
