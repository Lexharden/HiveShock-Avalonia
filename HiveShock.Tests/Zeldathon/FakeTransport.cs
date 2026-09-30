using System.Threading.Channels;
using HiveShock.Zeldathon;

namespace HiveShock.Tests.Zeldathon;

/// <summary>Servidor de mentira: guarda lo enviado y deja al test decidir qué responde.</summary>
public sealed class FakeTransport : IZeldathonTransport
{
    private readonly Channel<string?> _inbound = Channel.CreateUnbounded<string?>();
    private readonly List<string> _sent = [];
    private readonly object _gate = new();

    public Exception? FailConnect { get; set; }

    public bool Disposed { get; private set; }

    public List<string> Sent
    {
        get
        {
            lock (_gate)
            {
                return [.. _sent];
            }
        }
    }

    /// <summary>Se llama con cada texto enviado por el cliente; sirve para responder automáticamente.</summary>
    public Action<FakeTransport, string>? OnSent { get; set; }

    public void Push(string json) => _inbound.Writer.TryWrite(json);

    /// <summary>El servidor cierra la conexión.</summary>
    public void Close() => _inbound.Writer.TryWrite(null);

    public Task ConnectAsync(Uri uri, string token, CancellationToken ct) =>
        FailConnect != null ? Task.FromException(FailConnect) : Task.CompletedTask;

    public Task SendAsync(string text, CancellationToken ct)
    {
        lock (_gate)
        {
            _sent.Add(text);
        }

        OnSent?.Invoke(this, text);
        return Task.CompletedTask;
    }

    public async Task<string?> ReceiveAsync(CancellationToken ct) => await _inbound.Reader.ReadAsync(ct);

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

public sealed class FakeTransportFactory : IZeldathonTransportFactory
{
    private readonly object _gate = new();
    private readonly List<FakeTransport> _created = [];

    /// <summary>Prepara cada conexión nueva (por número, desde 0).</summary>
    public Action<int, FakeTransport>? Configure { get; set; }

    public List<FakeTransport> Created
    {
        get
        {
            lock (_gate)
            {
                return [.. _created];
            }
        }
    }

    public IZeldathonTransport Create()
    {
        var t = new FakeTransport();
        int index;
        lock (_gate)
        {
            index = _created.Count;
            _created.Add(t);
        }

        Configure?.Invoke(index, t);
        return t;
    }
}
