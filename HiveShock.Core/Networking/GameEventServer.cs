using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using HiveShock.Configuration;
using HiveShock.Logging;

namespace HiveShock.Networking;

public sealed class GameEventArgs : EventArgs
{
    public required string Event { get; init; }
    public int Deaths { get; init; }
}

/// <summary>Escucha JSON del juego en EVENT_PORT (default 43002).</summary>
public sealed class GameEventServer : IAsyncDisposable
{
    private readonly BridgeOptions _options;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _runTask;

    public event EventHandler<GameEventArgs>? EventReceived;

    public GameEventServer(BridgeOptions options) => _options = options;

    public void Start()
    {
        if (_runTask != null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Loopback, _options.EventPort);
        _listener.Start();
        BridgeLog.Info($"Eventos juego escuchando 127.0.0.1:{_options.EventPort}");
        _runTask = RunAsync(_cts.Token);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener!.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                _ = HandleClientAsync(client, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            BridgeLog.Warn($"Eventos: {ex.Message}");
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            using var _ = client;
            using var reader = new StreamReader(client.GetStream(), Encoding.UTF8);
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                TryDispatch(line);
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
        catch (Exception ex)
        {
            BridgeLog.Warn($"Eventos cliente: {ex.Message}");
        }
    }

    private void TryDispatch(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var eventName = root.TryGetProperty("event", out var ev) ? ev.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(eventName))
            {
                return;
            }

            var deaths = 0;
            if (root.TryGetProperty("deaths", out var d) && d.TryGetInt32(out var n))
            {
                deaths = n;
            }

            EventReceived?.Invoke(this, new GameEventArgs { Event = eventName, Deaths = deaths });
        }
        catch (Exception ex)
        {
            BridgeLog.Warn($"Eventos JSON: {ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            _cts?.Cancel();
            _listener?.Stop();
            if (_runTask != null)
            {
                try
                {
                    await _runTask.ConfigureAwait(false);
                }
                catch
                {
                    // ignore
                }
            }
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            _listener = null;
            _runTask = null;
        }
    }
}
