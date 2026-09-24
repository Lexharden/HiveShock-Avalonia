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
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
        });
    }

    public bool TryEnqueue(TtsMessage message) => _channel.Writer.TryWrite(message);

    public IAsyncEnumerable<TtsMessage> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);
}
