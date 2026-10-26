using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using HiveShock.Logging;

namespace HiveShock.Overlays;

/// <summary>
/// Servidor HTTP mínimo para los overlays de OBS («Fuente de navegador»). Sirve las páginas de
/// <c>/overlay/{contador|regalos|metas|alertas}</c>, un flujo SSE en <c>/events</c> con el estado y los avisos, y las
/// imágenes de regalos registradas en <c>/img/…</c>. Va sobre <see cref="TcpListener"/> a propósito: así puede escuchar
/// en la red local sin permisos de administrador (HttpListener los pide para direcciones que no sean localhost).
/// </summary>
public sealed class WebOverlayServer : IDisposable
{
    private const int MaxClients = 32;
    private const int MaxHeadBytes = 8 * 1024;
    private const int MaxImages = 4000;
    private static readonly TimeSpan HeadTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(15);

    private static readonly Lazy<byte[]> PageBytes = new(LoadPage);

    private readonly ConcurrentDictionary<string, string> _snapshots = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _images = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly List<Channel<string>> _clients = [];
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;
    private string _token = "";

    public bool IsRunning => _listener != null;

    public int Port { get; private set; }

    public bool AllowLan { get; private set; }

    /// <summary>Con red local activada hace falta <c>?k=token</c> en todas las peticiones.</summary>
    public string Token => _token;

    public int ClientCount
    {
        get
        {
            lock (_gate)
            {
                return _clients.Count;
            }
        }
    }

    /// <summary>Arranca el servidor. Devuelve false (y lo deja registrado) si el puerto está ocupado.</summary>
    public bool Start(int port, bool allowLan, string? token)
    {
        Stop();
        try
        {
            var listener = new TcpListener(allowLan ? IPAddress.Any : IPAddress.Loopback, port);
            listener.Start();
            _listener = listener;
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            AllowLan = allowLan;
            _token = allowLan ? token ?? "" : "";
            _cts = new CancellationTokenSource();
            _acceptLoop = Task.Run(() => AcceptLoopAsync(listener, _cts.Token));
            BridgeLog.Info($"Overlays web en http://127.0.0.1:{Port}/ ({(allowLan ? "red local" : "solo este equipo")})");
            return true;
        }
        catch (SocketException ex)
        {
            BridgeLog.Warn($"Overlays web: no se pudo abrir el puerto {port} ({ex.SocketErrorCode}). Cambia el puerto en Estudio.");
            return false;
        }
    }

    public void Stop()
    {
        var listener = _listener;
        _listener = null;
        if (listener == null)
        {
            return;
        }

        try
        {
            _cts?.Cancel();
            listener.Stop();
        }
        catch (Exception)
        {
            // Ya estaba parado.
        }

        lock (_gate)
        {
            foreach (var client in _clients)
            {
                client.Writer.TryComplete();
            }

            _clients.Clear();
        }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }

    /// <summary>Guarda el último estado de un canal; los clientes nuevos lo reciben al conectar.</summary>
    public void Publish(string channel, string json)
    {
        if (_snapshots.TryGetValue(channel, out var previous) && previous == json)
        {
            return;
        }

        _snapshots[channel] = json;
        Broadcast(Frame(channel, json));
    }

    /// <summary>Aviso puntual (alerta): se envía a los clientes conectados y no se guarda.</summary>
    public void Push(string channel, string json) => Broadcast(Frame(channel, json));

    /// <summary>
    /// Deja una imagen local disponible por URL. Solo se sirven las registradas, nunca rutas arbitrarias.
    /// Devuelve null si no existe o no es una imagen.
    /// </summary>
    public string? RegisterImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ContentTypeOf(ext) == null)
        {
            return null;
        }

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception)
        {
            return null;
        }

        if (!File.Exists(full))
        {
            return null;
        }

        var name = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(full.ToLowerInvariant())))[..16].ToLowerInvariant() + ext;
        if (_images.Count >= MaxImages && !_images.ContainsKey(name))
        {
            return null;
        }

        _images[name] = full;
        return "/img/" + name;
    }

    /// <summary>Direcciones listas para pegar en OBS.</summary>
    public string UrlFor(string overlay, string? host = null)
    {
        var url = $"http://{host ?? "127.0.0.1"}:{Port}/overlay/{overlay}";
        return _token.Length > 0 ? url + "?k=" + _token : url;
    }

    private static string Frame(string channel, string json) => $"event: {channel}\ndata: {json}\n\n";

    private void Broadcast(string frame)
    {
        lock (_gate)
        {
            for (var i = _clients.Count - 1; i >= 0; i--)
            {
                // Un cliente que no lee (pestaña colgada) se desconecta en vez de acumular memoria.
                if (!_clients[i].Writer.TryWrite(frame))
                {
                    _clients[i].Writer.TryComplete();
                    _clients.RemoveAt(i);
                }
            }
        }
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch (Exception) when (ct.IsCancellationRequested || !IsRunning)
            {
                return;
            }
            catch (Exception ex)
            {
                BridgeLog.Warn($"Overlays web: {ex.Message}");
                await Task.Delay(250, ct).ConfigureAwait(false);
                continue;
            }

            _ = Task.Run(() => HandleAsync(client, ct), ct);
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                client.NoDelay = true;
                var stream = client.GetStream();
                var request = await ReadRequestAsync(stream, ct).ConfigureAwait(false);
                if (request == null)
                {
                    return;
                }

                await RouteAsync(stream, request.Value.Method, request.Value.Target, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
            {
                // El navegador cerró la conexión: es lo normal con SSE.
            }
            catch (Exception ex)
            {
                BridgeLog.Warn($"Overlays web: {ex.Message}");
            }
        }
    }

    private static async Task<(string Method, string Target)?> ReadRequestAsync(NetworkStream stream, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(HeadTimeout);
        var buffer = new byte[MaxHeadBytes];
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read), timeout.Token).ConfigureAwait(false);
            if (n == 0)
            {
                return null;
            }

            read += n;
            if (read >= 4 && buffer.AsSpan(0, read).IndexOf("\r\n\r\n"u8) >= 0)
            {
                break;
            }
        }

        var head = Encoding.ASCII.GetString(buffer, 0, read);
        var line = head[..Math.Max(0, head.IndexOf("\r\n", StringComparison.Ordinal))];
        var parts = line.Split(' ');
        return parts.Length >= 2 ? (parts[0], parts[1]) : null;
    }

    private async Task RouteAsync(NetworkStream stream, string method, string target, CancellationToken ct)
    {
        var head = string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase);
        if (!head && !string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
        {
            await WriteSimpleAsync(stream, 405, "text/plain", "Method Not Allowed"u8.ToArray(), false, ct).ConfigureAwait(false);
            return;
        }

        var q = target.IndexOf('?', StringComparison.Ordinal);
        var path = Uri.UnescapeDataString(q >= 0 ? target[..q] : target);
        var query = q >= 0 ? target[(q + 1)..] : "";

        if (_token.Length > 0 && !TokenMatches(query))
        {
            await WriteSimpleAsync(stream, 403, "text/plain", "Falta la clave (?k=...) de la URL."u8.ToArray(), head, ct).ConfigureAwait(false);
            return;
        }

        if (path == "/events")
        {
            await ServeEventsAsync(stream, ct).ConfigureAwait(false);
            return;
        }

        if (path.StartsWith("/img/", StringComparison.Ordinal))
        {
            await ServeImageAsync(stream, path[5..], head, ct).ConfigureAwait(false);
            return;
        }

        if (path is "/overlay/contador" or "/overlay/regalos" or "/overlay/metas" or "/overlay/alertas")
        {
            await WriteSimpleAsync(stream, 200, "text/html; charset=utf-8", PageBytes.Value, head, ct).ConfigureAwait(false);
            return;
        }

        if (path is "/" or "/index.html")
        {
            await WriteSimpleAsync(stream, 200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(IndexPage(query)), head, ct)
                .ConfigureAwait(false);
            return;
        }

        await WriteSimpleAsync(stream, 404, "text/plain", "No encontrado"u8.ToArray(), head, ct).ConfigureAwait(false);
    }

    private bool TokenMatches(string query)
    {
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=', StringComparison.Ordinal);
            if (eq > 0 && pair[..eq] == "k")
            {
                var given = Encoding.UTF8.GetBytes(Uri.UnescapeDataString(pair[(eq + 1)..]));
                return CryptographicOperations.FixedTimeEquals(given, Encoding.UTF8.GetBytes(_token));
            }
        }

        return false;
    }

    private string IndexPage(string query)
    {
        var suffix = query.Length > 0 ? "?" + query : "";
        var sb = new StringBuilder("<!doctype html><meta charset=utf-8><title>HiveShock</title>")
            .Append("<body style=\"font:16px system-ui;background:#0E1520;color:#F7F7F7;padding:24px\">")
            .Append("<h2>HiveShock · overlays para OBS</h2><p>Copia la dirección en una «Fuente de navegador».</p><ul>");
        foreach (var name in new[] { "contador", "regalos", "metas", "alertas" })
        {
            sb.Append($"<li><a style=\"color:#EBA00A\" href=\"/overlay/{name}{suffix}\">/overlay/{name}</a></li>");
        }

        return sb.Append("</ul></body>").ToString();
    }

    private async Task ServeEventsAsync(NetworkStream stream, CancellationToken ct)
    {
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(256)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait,
        });

        var full = false;
        lock (_gate)
        {
            full = _clients.Count >= MaxClients;
            if (!full)
            {
                AddClient(channel);
            }
        }

        if (full)
        {
            await WriteSimpleAsync(stream, 503, "text/plain", "Demasiadas conexiones"u8.ToArray(), false, ct).ConfigureAwait(false);
            return;
        }

        try
        {
            var header = "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nCache-Control: no-cache\r\n" +
                         "Connection: keep-alive\r\nX-Accel-Buffering: no\r\n\r\nretry: 2000\n\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(header), ct).ConfigureAwait(false);

            while (!ct.IsCancellationRequested)
            {
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
                wait.CancelAfter(KeepAlive);
                string frame;
                try
                {
                    if (!await channel.Reader.WaitToReadAsync(wait.Token).ConfigureAwait(false))
                    {
                        return;
                    }

                    frame = await channel.Reader.ReadAsync(wait.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    frame = ": ping\n\n"; // evita que OBS o un proxy cierren la conexión por inactividad
                }

                await stream.WriteAsync(Encoding.UTF8.GetBytes(frame), ct).ConfigureAwait(false);
            }
        }
        finally
        {
            lock (_gate)
            {
                _clients.Remove(channel);
            }
        }
    }

    /// <summary>Con <c>_gate</c> tomado: primero el estado actual, luego lo que vaya llegando, sin huecos entre medias.</summary>
    private void AddClient(Channel<string> channel)
    {
        foreach (var snapshot in _snapshots)
        {
            channel.Writer.TryWrite(Frame(snapshot.Key, snapshot.Value));
        }

        _clients.Add(channel);
    }

    private async Task ServeImageAsync(NetworkStream stream, string name, bool head, CancellationToken ct)
    {
        if (!_images.TryGetValue(name, out var file) || !File.Exists(file))
        {
            await WriteSimpleAsync(stream, 404, "text/plain", "No encontrado"u8.ToArray(), head, ct).ConfigureAwait(false);
            return;
        }

        var type = ContentTypeOf(Path.GetExtension(file).ToLowerInvariant()) ?? "application/octet-stream";
        var bytes = await File.ReadAllBytesAsync(file, ct).ConfigureAwait(false);
        await WriteSimpleAsync(stream, 200, type, bytes, head, ct, "Cache-Control: max-age=3600\r\n").ConfigureAwait(false);
    }

    private static async Task WriteSimpleAsync(
        NetworkStream stream, int status, string contentType, byte[] body, bool head, CancellationToken ct, string extra = "")
    {
        var reason = status switch { 200 => "OK", 403 => "Forbidden", 404 => "Not Found", 405 => "Method Not Allowed", _ => "Error" };
        var header = $"HTTP/1.1 {status} {reason}\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\n" +
                     $"{extra}Connection: close\r\nX-Content-Type-Options: nosniff\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header), ct).ConfigureAwait(false);
        if (!head)
        {
            await stream.WriteAsync(body, ct).ConfigureAwait(false);
        }
    }

    private static string? ContentTypeOf(string ext) => ext switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        _ => null,
    };

    private static byte[] LoadPage()
    {
        using var stream = typeof(WebOverlayServer).Assembly.GetManifestResourceStream("overlay.html");
        if (stream == null)
        {
            return "overlay.html no encontrado"u8.ToArray();
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
