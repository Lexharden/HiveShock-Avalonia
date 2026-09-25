namespace HiveShock.Voice.Piper.Synthesis;

/// <summary>
/// Procesos de Piper vivos, uno por voz y velocidad, con un máximo (cada voz cargada ocupa
/// ~100 MB). Cuando hace falta uno nuevo y el pool está lleno, se cierra el menos usado: con
/// voz aleatoria entre muchas voces Piper eso implica recargas (0,3–0,7 s), que la precarga
/// de SmartVoiceManager oculta en parte.
/// </summary>
internal sealed class PiperWorkerPool : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, PiperWorker> _workers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<PiperWorkerOptions, PiperWorker> _factory;

    public PiperWorkerPool(int maxWorkers = 3, Func<PiperWorkerOptions, PiperWorker>? factory = null)
    {
        MaxWorkers = Math.Max(1, maxWorkers);
        _factory = factory ?? (options => new PiperWorker(options));
    }

    public int MaxWorkers { get; }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _workers.Count;
            }
        }
    }

    public PiperWorker Get(PiperWorkerOptions options)
    {
        PiperWorker? evicted = null;
        PiperWorker worker;
        lock (_gate)
        {
            if (_workers.TryGetValue(options.Key, out var existing))
            {
                existing.Touch();
                return existing;
            }

            if (_workers.Count >= MaxWorkers)
            {
                var oldest = _workers.Values.MinBy(w => w.LastUsedUtc)!;
                _workers.Remove(oldest.Options.Key);
                evicted = oldest;
            }

            worker = _factory(options);
            _workers[options.Key] = worker;
        }

        evicted?.Dispose();
        return worker;
    }

    /// <summary>Cierra todos los procesos (al borrar voces o desinstalar el motor).</summary>
    public void Clear()
    {
        List<PiperWorker> all;
        lock (_gate)
        {
            all = [.. _workers.Values];
            _workers.Clear();
        }

        foreach (var worker in all)
        {
            worker.Dispose();
        }
    }

    public void Dispose() => Clear();
}
