using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using HiveShock.Logging;

namespace HiveShock.Live;

public sealed class TwitchEventSubClient
{
    private const string DefaultSocket = "wss://eventsub.wss.twitch.tv/ws";

    public async Task RunAsync(
        string clientId,
        string accessToken,
        string userId,
        Action<string, string> onChat,
        Action<string> onFollow,
        Action<string, int> onCheer,
        Action connected,
        CancellationToken ct)
    {
        var url = DefaultSocket;
        while (!ct.IsCancellationRequested)
        {
            using var socket = new ClientWebSocket();
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            await socket.ConnectAsync(new Uri(url), ct).ConfigureAwait(false);
            url = DefaultSocket;

            string? sessionId = null;
            var keepalive = TimeSpan.FromSeconds(15);

            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                string? raw;
                try
                {
                    using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    wait.CancelAfter(keepalive);
                    raw = await ReceiveTextAsync(socket, wait.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException)
                {
                    BridgeLog.Warn("Twitch: sin keepalive, reconectando.");
                    break;
                }
                catch (WebSocketException ex)
                {
                    BridgeLog.Warn($"Twitch socket: {ex.Message}");
                    break;
                }

                if (raw == null)
                {
                    break;
                }
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                var type = root.GetProperty("metadata").GetProperty("message_type").GetString();
                switch (type)
                {
                    case "session_welcome":
                        sessionId = root.GetProperty("payload").GetProperty("session").GetProperty("id").GetString();
                        var timeout = root.GetProperty("payload").GetProperty("session")
                            .TryGetProperty("keepalive_timeout_seconds", out var ks)
                            ? ks.GetInt32()
                            : 10;
                        keepalive = TimeSpan.FromSeconds(Math.Max(10, timeout + 5));
                        await SubscribeAsync(clientId, accessToken, userId, sessionId!, ct).ConfigureAwait(false);
                        connected();
                        break;
                    case "session_keepalive":
                        break;
                    case "session_reconnect":
                        url = root.GetProperty("payload").GetProperty("session").GetProperty("reconnect_url").GetString()
                              ?? DefaultSocket;
                        try
                        {
                            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "reconnect", ct)
                                .ConfigureAwait(false);
                        }
                        catch
                        {
                            // ignore
                        }

                        goto NextSocket;
                    case "notification":
                        HandleNotification(root, onChat, onFollow, onCheer);
                        break;
                    case "revocation":
                        throw new InvalidOperationException("Twitch canceló la suscripción. Vuelve a conectar.");
                }
            }

            NextSocket:
            if (ct.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private static void HandleNotification(
        JsonElement root,
        Action<string, string> onChat,
        Action<string> onFollow,
        Action<string, int> onCheer)
    {
        var subType = root.GetProperty("metadata").TryGetProperty("subscription_type", out var st)
            ? st.GetString()
            : "";
        if (!root.TryGetProperty("payload", out var payload) ||
            !payload.TryGetProperty("event", out var evt))
        {
            return;
        }

        if (string.Equals(subType, "channel.chat.message", StringComparison.OrdinalIgnoreCase))
        {
            var user = evt.TryGetProperty("chatter_user_name", out var n) ? n.GetString() ?? "" : "";
            var text = "";
            if (evt.TryGetProperty("message", out var message) &&
                message.TryGetProperty("text", out var t))
            {
                text = t.GetString() ?? "";
            }

            onChat(user, text);
            return;
        }

        if (string.Equals(subType, "channel.follow", StringComparison.OrdinalIgnoreCase))
        {
            var user = evt.TryGetProperty("user_name", out var n) ? n.GetString() ?? "" : "";
            onFollow(user);
            return;
        }

        if (string.Equals(subType, "channel.cheer", StringComparison.OrdinalIgnoreCase))
        {
            var bits = 0;
            if (evt.TryGetProperty("bits", out var bitsEl))
            {
                bitsEl.TryGetInt32(out bits);
            }

            if (bits <= 0)
            {
                return;
            }

            var anonymous = evt.TryGetProperty("is_anonymous", out var anon) &&
                            anon.ValueKind == JsonValueKind.True;
            var user = "";
            if (!anonymous)
            {
                user = evt.TryGetProperty("user_name", out var n) ? n.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(user) && evt.TryGetProperty("user_login", out var login))
                {
                    user = login.GetString() ?? "";
                }
            }

            onCheer(user, bits);
        }
    }

    private static async Task SubscribeAsync(
        string clientId,
        string accessToken,
        string userId,
        string sessionId,
        CancellationToken ct)
    {
        await CreateSubscriptionAsync(
            clientId,
            accessToken,
            sessionId,
            "channel.chat.message",
            "1",
            new Dictionary<string, string>
            {
                ["broadcaster_user_id"] = userId,
                ["user_id"] = userId,
            },
            required: true,
            ct).ConfigureAwait(false);

        try
        {
            await CreateSubscriptionAsync(
                clientId,
                accessToken,
                sessionId,
                "channel.follow",
                "2",
                new Dictionary<string, string>
                {
                    ["broadcaster_user_id"] = userId,
                    ["moderator_user_id"] = userId,
                },
                required: false,
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            BridgeLog.Warn($"Twitch follow: {ex.Message}");
        }

        try
        {
            await CreateSubscriptionAsync(
                clientId,
                accessToken,
                sessionId,
                "channel.cheer",
                "1",
                new Dictionary<string, string>
                {
                    ["broadcaster_user_id"] = userId,
                },
                required: false,
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            BridgeLog.Warn($"Twitch bits: {ex.Message}");
        }
    }

    private static async Task CreateSubscriptionAsync(
        string clientId,
        string accessToken,
        string sessionId,
        string type,
        string version,
        Dictionary<string, string> condition,
        bool required,
        CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.twitch.tv/helix/eventsub/subscriptions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("Client-Id", clientId);
        var payload = new Dictionary<string, object?>
        {
            ["type"] = type,
            ["version"] = version,
            ["condition"] = condition,
            ["transport"] = new Dictionary<string, string>
            {
                ["method"] = "websocket",
                ["session_id"] = sessionId,
            },
        };
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            return;
        }

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var message = "Twitch no aceptó la suscripción.";
        try
        {
            using var doc = JsonDocument.Parse(json);
            message = TwitchApi.ReadHelixError(doc.RootElement, message);
        }
        catch
        {
            // keep fallback
        }

        if (required)
        {
            throw new InvalidOperationException(message);
        }

        throw new InvalidOperationException(message);
    }

    private static async Task<string?> ReceiveTextAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        using var ms = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            ms.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }
    }
}
