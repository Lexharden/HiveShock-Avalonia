namespace HiveShock.Networking;

/// <summary>
/// Sigue los efectos de un mismo disparador hasta que todos terminan (entregados o descartados) y avisa una vez con
/// si llegó al juego al menos uno. Lo usa un canje de puntos de Twitch para decidir: completarlo o devolver los puntos.
/// Seguro entre hilos: los efectos los resuelven el despachador, las esperas en cadena y la desconexión.
/// </summary>
public sealed class PlanTracker
{
    private readonly Action<bool> _onDone;
    private int _remaining;
    private int _delivered;
    private int _finished;

    /// <param name="steps">Cuántos efectos se van a informar (uno por cada efecto encolado).</param>
    /// <param name="onDone">Recibe true si se entregó al menos uno. Se llama una sola vez.</param>
    public PlanTracker(int steps, Action<bool> onDone)
    {
        _onDone = onDone;
        _remaining = Math.Max(0, steps);
        if (_remaining == 0)
        {
            Finish();
        }
    }

    public bool IsFinished => Volatile.Read(ref _finished) == 1;

    /// <summary>Efectos entregados hasta ahora (para decidir al desconectar con el plan a medias).</summary>
    public int Delivered => Volatile.Read(ref _delivered);

    /// <summary>Un efecto terminó: <paramref name="delivered"/> true si llegó al juego (o a su servicio: teclas, sistema).</summary>
    public void Report(bool delivered)
    {
        if (delivered)
        {
            Interlocked.Increment(ref _delivered);
        }

        if (Interlocked.Decrement(ref _remaining) == 0)
        {
            Finish();
        }
    }

    /// <summary>Los <paramref name="count"/> efectos que faltaban ya no saldrán (pausa, desconexión).</summary>
    public void ReportDiscarded(int count)
    {
        for (var i = 0; i < count; i++)
        {
            Report(false);
        }
    }

    private void Finish()
    {
        if (Interlocked.Exchange(ref _finished, 1) == 0)
        {
            _onDone(Volatile.Read(ref _delivered) > 0);
        }
    }
}
