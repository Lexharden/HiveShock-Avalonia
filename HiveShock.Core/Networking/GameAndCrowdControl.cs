using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using HiveShock.Configuration;
using HiveShock.Live;
using HiveShock.Logging;

namespace HiveShock.Networking;

public sealed class GameTcpClient
{
    private readonly BridgeOptions _options;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private long _earliestNextSendMs;

    public GameTcpClient(BridgeOptions options) => _options = options;

    /// <summary>Tiempo máximo que el despachador reintenta un efecto cuando el juego no responde.</summary>
    public TimeSpan RetryWindow => _options.GameRetryWindow;

    public async Task SendAsync(IReadOnlyDictionary<string, object?> command, CancellationToken ct)
    {
        var line = JsonSerializer.Serialize(command, JsonDefaults.Options) + "\n";

        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await WaitForGapAsync(ct).ConfigureAwait(false);

            if (_options.DryRun)
            {
                BridgeLog.Info($"Dry-run {line.TrimEnd()}");
                MarkSent();
                return;
            }

            using var client = new TcpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_options.GameConnectTimeout);

            try
            {
                await client.ConnectAsync(_options.GameHost, _options.GamePort, timeoutCts.Token)
                    .ConfigureAwait(false);
                var stream = client.GetStream();
                var bytes = Encoding.UTF8.GetBytes(line);
                await stream.WriteAsync(bytes, timeoutCts.Token).ConfigureAwait(false);
                await stream.FlushAsync(timeoutCts.Token).ConfigureAwait(false);
                MarkSent();
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"Timeout juego {_options.GameHost}:{_options.GamePort}");
            }
            catch (SocketException ex)
            {
                throw new InvalidOperationException($"Juego: {ex.Message}", ex);
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public Task SendActionAsync(string action, CancellationToken ct = default) =>
        SendAsync(new Dictionary<string, object?> { ["action"] = action }, ct);

    private async Task WaitForGapAsync(CancellationToken ct)
    {
        var gapMs = _options.EffectGapMs;
        if (gapMs <= 0)
        {
            return;
        }

        var wait = _earliestNextSendMs - Environment.TickCount64;
        if (wait > 0)
        {
            await Task.Delay((int)Math.Min(wait, int.MaxValue), ct).ConfigureAwait(false);
        }
    }

    private void MarkSent()
    {
        var gapMs = _options.EffectGapMs;
        _earliestNextSendMs = gapMs > 0
            ? Environment.TickCount64 + gapMs
            : 0;
    }
}

public sealed class EffectDispatcher : IDisposable
{
    private readonly Channel<EffectWorkItem> _channel = Channel.CreateUnbounded<EffectWorkItem>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    private readonly EffectCatalog _effects;
    private readonly GameTcpClient _game;
    private readonly ViewerGuard? _guard;
    private int _pendingChat;
    private int _generation;

    public EffectDispatcher(EffectCatalog effects, GameTcpClient game, ViewerGuard? guard = null)
    {
        _effects = effects;
        _game = game;
        _guard = guard;
        if (_guard != null)
        {
            _guard.PausedChanged += OnPausedChanged;
        }
    }

    public void Dispose()
    {
        if (_guard != null)
        {
            _guard.PausedChanged -= OnPausedChanged;
        }
    }

    /// <summary>Al pausar, lo que ya estaba en cola no debe llegar al juego.</summary>
    private void OnPausedChanged(bool paused)
    {
        if (paused)
        {
            DiscardPending();
        }
    }

    /// <summary>
    /// Descarta lo que aún espera turno (no lo que ya se está enviando). Se marca por generación en vez de
    /// vaciar el canal: tiene un solo lector y vaciarlo desde otro hilo no es seguro.
    /// </summary>
    public void DiscardPending() => Interlocked.Increment(ref _generation);

    public int PendingChat => Volatile.Read(ref _pendingChat);

    public async ValueTask EnqueueAsync(
        string effectId,
        string? user,
        string reason,
        CancellationToken ct = default,
        bool fromChat = false,
        IReadOnlyDictionary<string, double>? paramOverrides = null)
    {
        var cmd = _effects.Resolve(effectId, user, paramOverrides);
        if (cmd is null)
        {
            BridgeLog.Warn($"Efecto desconocido: {effectId}");
            return;
        }

        if (fromChat)
        {
            Interlocked.Increment(ref _pendingChat);
        }

        try
        {
            var generation = Volatile.Read(ref _generation);
            await _channel.Writer.WriteAsync(new EffectWorkItem(effectId, user, reason, cmd, fromChat, generation), ct)
                .ConfigureAwait(false);
        }
        catch
        {
            if (fromChat)
            {
                Interlocked.Decrement(ref _pendingChat);
            }

            throw;
        }
    }

    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var item in _channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    if (item.Generation != Volatile.Read(ref _generation))
                    {
                        BridgeLog.Info($"Descartado {item.EffectId} <- {item.User ?? "anon"} ({item.Reason}): efectos en pausa");
                        continue;
                    }

                    if (!await SendWithRetryAsync(item, ct).ConfigureAwait(false))
                    {
                        continue;
                    }

                    BridgeLog.Info($"OK {item.EffectId} <- {item.User ?? "anon"} ({item.Reason})");
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    BridgeLog.Error($"Fallo {item.EffectId}: {ex.Message}");
                }
                finally
                {
                    if (item.FromChat)
                    {
                        Interlocked.Decrement(ref _pendingChat);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Desconectar cancela el lector del canal a propósito.
        }
    }

    /// <summary>
    /// Entrega el efecto; si el juego no está disponible reintenta con espera creciente (1 s → 10 s) hasta
    /// <see cref="GameTcpClient.RetryWindow"/>. Mantiene el orden: lo que viene detrás espera. Devuelve false si se
    /// descartó porque los efectos se pusieron en pausa mientras tanto.
    /// </summary>
    private async Task<bool> SendWithRetryAsync(EffectWorkItem item, CancellationToken ct)
    {
        var window = _game.RetryWindow;
        var started = Environment.TickCount64;
        var delayMs = 1000;
        var warned = false;

        while (true)
        {
            try
            {
                await _game.SendAsync(item.Command, ct).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex) when ((ex is TimeoutException || ex is InvalidOperationException) &&
                                       window > TimeSpan.Zero)
            {
                if (item.Generation != Volatile.Read(ref _generation))
                {
                    BridgeLog.Info($"Descartado {item.EffectId} <- {item.User ?? "anon"} ({item.Reason}): efectos en pausa");
                    return false;
                }

                if (Environment.TickCount64 - started >= window.TotalMilliseconds)
                {
                    throw;
                }

                if (!warned)
                {
                    warned = true;
                    BridgeLog.Warn(
                        $"Juego no disponible ({ex.Message}); reintentando {item.EffectId} <- {item.User ?? "anon"} " +
                        $"hasta {window.TotalMinutes:0.#} min");
                }

                await Task.Delay(delayMs, ct).ConfigureAwait(false);
                delayMs = Math.Min(delayMs * 2, 10_000);
            }
        }
    }

    private sealed record EffectWorkItem(
        string EffectId,
        string? User,
        string Reason,
        Dictionary<string, object?> Command,
        bool FromChat,
        int Generation);
}

public sealed class CrowdControlServer
{
    private readonly BridgeOptions _options;
    private readonly EffectCatalog _effects;
    private readonly EffectDispatcher _dispatcher;

    public CrowdControlServer(BridgeOptions options, EffectCatalog effects, EffectDispatcher dispatcher)
    {
        _options = options;
        _effects = effects;
        _dispatcher = dispatcher;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, _options.CrowdControlPort);
        listener.Start();
        BridgeLog.Info($"Pruebas escuchando 127.0.0.1:{_options.CrowdControlPort}");

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                _ = Task.Run(() => HandleClientAsync(client, ct), CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
            // Desconectar
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        BridgeLog.Info("CC conectado");
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var buffer = new byte[8192];
                var pending = new StringBuilder();

                while (!ct.IsCancellationRequested)
                {
                    var read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    pending.Append(Encoding.UTF8.GetString(buffer, 0, read));
                    var text = pending.ToString();
                    var parts = text.Split(['\n', '\0']);
                    pending.Clear();
                    if (!text.EndsWith('\n') && !text.EndsWith('\0') && parts.Length > 0)
                    {
                        pending.Append(parts[^1]);
                        parts = parts[..^1];
                    }

                    foreach (var raw in parts)
                    {
                        if (string.IsNullOrWhiteSpace(raw))
                        {
                            continue;
                        }

                        await ProcessMessageAsync(stream, raw.Trim(), ct).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                BridgeLog.Error($"CC: {ex.Message}");
            }
            finally
            {
                BridgeLog.Info("CC desconectado");
            }
        }
    }

    private async Task ProcessMessageAsync(NetworkStream stream, string raw, CancellationToken ct)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(raw);
        }
        catch
        {
            return;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (!root.TryGetProperty("code", out var codeEl) || codeEl.ValueKind != JsonValueKind.String)
            {
                if (root.TryGetProperty("id", out var idOnly))
                {
                    await WriteCcAsync(stream, new { id = JsonElementToObject(idOnly), status = 0 }, ct)
                        .ConfigureAwait(false);
                }

                return;
            }

            var code = codeEl.GetString() ?? "";
            object? id = root.TryGetProperty("id", out var idEl) ? JsonElementToObject(idEl) : null;
            var viewer = root.TryGetProperty("viewer", out var viewerEl) && viewerEl.ValueKind == JsonValueKind.String
                ? viewerEl.GetString()
                : null;

            if (!_effects.Contains(code))
            {
                BridgeLog.Warn($"CC efecto desconocido: {code}");
                await WriteCcAsync(stream, new { id, status = 1 }, ct).ConfigureAwait(false);
                return;
            }

            try
            {
                await _dispatcher.EnqueueAsync(code, viewer, "Pruebas", ct).ConfigureAwait(false);
                await WriteCcAsync(stream, new { id, status = 0 }, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                BridgeLog.Error($"CC: {ex.Message}");
                await WriteCcAsync(stream, new { id, status = 1 }, ct).ConfigureAwait(false);
            }
        }
    }

    private static async Task WriteCcAsync(NetworkStream stream, object payload, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonDefaults.Options) + "\0");
        await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
    }

    private static object? JsonElementToObject(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number when el.TryGetInt64(out var l) => l,
        JsonValueKind.Number => el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => el.GetRawText(),
    };
}
