using System.Threading.Channels;

namespace HiveShock.Voice;

/// <summary>
/// Cola FIFO anti-spam para Smart TTS — mismo patrón que <c>EffectDispatcher</c> en
/// HiveShock.Core.Networking, pero acotada: si se llena, se descarta el mensaje más
/// antiguo en vez de bloquear al productor. Así la lectura nunca se atrasa respecto
/// al chat en vivo, a costa de saltarse comentarios viejos si hay mucho volumen.
/// </summary>
public sealed class SmartTtsQueue
{
    private readonly Channel<TtsMessage> _channel;

    public SmartTtsQueue(int capacity)
    {
        _channel = Channel.CreateBounded<TtsMessage>(new BoundedChannelOptions(Math.Max(1, capacity))
        {
            // Clear() lee desde el hilo de la UI a la vez que el consumidor: no es lector único.
            SingleReader = false,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
        });
    }

    /// <summary>Mensajes esperando turno (sin contar el que se está preparando o leyendo).</summary>
    public int Count => _channel.Reader.Count;

    public bool TryEnqueue(TtsMessage message) => _channel.Writer.TryWrite(message);

    public void Clear()
    {
        while (_channel.Reader.TryRead(out _))
        {
        }
    }

    public IAsyncEnumerable<TtsMessage> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);
}
