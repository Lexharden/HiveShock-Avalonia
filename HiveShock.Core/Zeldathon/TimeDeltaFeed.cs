using System.Diagnostics;

namespace HiveShock.Zeldathon;

/// <summary>Lo que el cronómetro debe mostrar ahora: el cambio neto del reloj por donaciones recientes.</summary>
/// <param name="Seconds">Suma con signo de lo que el servidor aplicó (positivo = más tiempo).</param>
/// <param name="Count">Cuántas donaciones se juntaron.</param>
/// <param name="Limited">Alguna quedó recortada por un tope del organizador.</param>
/// <param name="AgeMs">Milisegundos desde la última donación (la animación de entrada se reinicia con cada una).</param>
/// <param name="Version">Sube con cada donación nueva: así la pantalla sabe cuándo rehacer el texto.</param>
public sealed record TimeDeltaBurst(long Seconds, int Count, bool Limited, long AgeMs, long Version);

/// <summary>
/// Junta las donaciones que cambian el tiempo de la carrera para mostrarlas en el cronómetro. Varias
/// seguidas (una avalancha de regalos) se funden en un solo cambio neto en vez de apilar rótulos; si llegan
/// con espacio entre ellas, cada una tiene el suyo. Solo se anotan cambios que el servidor confirmó, con los
/// segundos que aplicó de verdad (no los que se pidieron), y no lleva nombres de espectadores.
/// </summary>
public sealed class TimeDeltaFeed
{
    /// <summary>Una donación que llega dentro de este margen de la anterior se suma a la misma ráfaga.</summary>
    public static readonly TimeSpan MergeWindow = TimeSpan.FromMilliseconds(1500);

    /// <summary>Cuánto dura visible tras la última donación (la animación termina aquí).</summary>
    public static readonly TimeSpan HoldFor = TimeSpan.FromMilliseconds(2600);

    /// <summary>Una confirmación de hace más que esto ya no es noticia (llegó tarde tras un corte).</summary>
    public static readonly TimeSpan MaxConfirmationAge = TimeSpan.FromSeconds(90);

    private readonly object _gate = new();
    private readonly Func<long> _nowMs;
    private readonly Func<DateTime> _utcNow;
    private long _seconds;
    private int _count;
    private bool _limited;
    private long _lastMs;
    private long _version;
    private bool _active;

    public TimeDeltaFeed(Func<long>? monotonicMs = null, Func<DateTime>? utcNow = null)
    {
        _nowMs = monotonicMs ?? (() => Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency);
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    /// <summary>Hay un cambio nuevo (puede llegar desde cualquier hilo).</summary>
    public event Action? Pushed;

    /// <summary>Una donación confirmada por el servidor. Se ignoran las que llegan muy tarde.</summary>
    public void Add(DonationTimeApplied applied)
    {
        if (_utcNow() - applied.CreatedUtc > MaxConfirmationAge)
        {
            return;
        }

        Push(applied.Seconds, applied.LimitedBy.Length > 0);
    }

    /// <summary>Anota un cambio de reloj (también lo usa «Probar animación»). 0 no es un cambio.</summary>
    public void Push(long seconds, bool limited)
    {
        if (seconds == 0)
        {
            return;
        }

        lock (_gate)
        {
            var now = _nowMs();
            if (_active && now - _lastMs <= MergeWindow.TotalMilliseconds)
            {
                _seconds += seconds;
                _count++;
                _limited |= limited;
            }
            else
            {
                _seconds = seconds;
                _count = 1;
                _limited = limited;
            }

            _active = true;
            _lastMs = now;
            _version++;
        }

        Pushed?.Invoke();
    }

    /// <summary>La ráfaga que se está mostrando, o null si ya pasó el tiempo.</summary>
    public TimeDeltaBurst? Current()
    {
        lock (_gate)
        {
            if (!_active)
            {
                return null;
            }

            var age = Math.Max(0, _nowMs() - _lastMs);
            if (age >= HoldFor.TotalMilliseconds)
            {
                _active = false;
                return null;
            }

            return new TimeDeltaBurst(_seconds, _count, _limited, age, _version);
        }
    }
}
