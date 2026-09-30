using System.Net;
using System.Net.WebSockets;
using System.Text;

namespace HiveShock.Zeldathon;

/// <summary>El servidor rechazó el token (HTTP 401): reintentar no sirve, hay que corregirlo.</summary>
public sealed class ZeldathonAuthException(string message) : Exception(message);

/// <summary>Canal de texto con el servidor. Detrás de una interfaz para probar el cliente sin red.</summary>
public interface IZeldathonTransport : IAsyncDisposable
{
    Task ConnectAsync(Uri uri, string token, CancellationToken ct);

    Task SendAsync(string text, CancellationToken ct);

    /// <summary>Siguiente mensaje de texto, o null si el servidor cerró.</summary>
    Task<string?> ReceiveAsync(CancellationToken ct);
}

public interface IZeldathonTransportFactory
{
    IZeldathonTransport Create();
}

public sealed class WebSocketTransportFactory : IZeldathonTransportFactory
{
    public IZeldathonTransport Create() => new WebSocketTransport();
}

public sealed class WebSocketTransport : IZeldathonTransport
{
    private const int MaxMessageBytes = 64 * 1024;

    private readonly ClientWebSocket _socket = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public async Task ConnectAsync(Uri uri, string token, CancellationToken ct)
    {
        _socket.Options.SetRequestHeader("Authorization", $"Bearer {token.Trim()}");
        _socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        try
        {
            await _socket.ConnectAsync(uri, ct).ConfigureAwait(false);
        }
        catch (WebSocketException) when (_socket.HttpStatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new ZeldathonAuthException("El servidor no aceptó el token.");
        }
    }

    public async Task SendAsync(string text, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task<string?> ReceiveAsync(CancellationToken ct)
    {
        var buffer = new byte[8192];
        using var message = new MemoryStream();
        while (true)
        {
            var result = await _socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            message.Write(buffer, 0, result.Count);
            if (message.Length > MaxMessageBytes)
            {
                throw new InvalidDataException("Mensaje del servidor demasiado grande.");
            }

            if (result.EndOfMessage)
            {
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    return Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                }

                message.SetLength(0);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", cts.Token).ConfigureAwait(false);
            }
        }
        catch
        {
            // cierre a medias: da igual
        }

        _socket.Dispose();
        _sendLock.Dispose();
    }
}
