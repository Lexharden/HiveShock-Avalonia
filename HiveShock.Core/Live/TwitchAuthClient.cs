using System.Net.Http.Headers;
using System.Text.Json;
using HiveShock.Logging;

namespace HiveShock.Live;

public sealed record TwitchDeviceStart(
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    int IntervalSeconds,
    int ExpiresInSeconds);

public sealed record TwitchTokenSet(string AccessToken, string RefreshToken);

public sealed record TwitchUser(string Id, string Login, string DisplayName);

public sealed class TwitchAuthClient : IDisposable
{
    public const string DefaultScopes =
        "user:read:chat user:bot channel:bot moderator:read:followers bits:read";

    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(20),
    };

    public async Task<TwitchDeviceStart> StartDeviceLoginAsync(string clientId, CancellationToken ct)
    {
        using var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["scopes"] = DefaultScopes,
        });
        using var response = await _http.PostAsync("https://id.twitch.tv/oauth2/device", body, ct).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(TwitchApi.ReadHelixError(root, "No se pudo empezar el login de Twitch."));
        }

        return new TwitchDeviceStart(
            DeviceCode: root.GetProperty("device_code").GetString() ?? "",
            UserCode: root.GetProperty("user_code").GetString() ?? "",
            VerificationUri: root.GetProperty("verification_uri").GetString() ?? "https://www.twitch.tv/activate",
            IntervalSeconds: root.TryGetProperty("interval", out var interval) ? interval.GetInt32() : 5,
            ExpiresInSeconds: root.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 1800);
    }

    public async Task<TwitchTokenSet?> PollDeviceTokenAsync(
        string clientId,
        string deviceCode,
        CancellationToken ct)
    {
        using var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["device_code"] = deviceCode,
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
        });
        using var response = await _http.PostAsync("https://id.twitch.tv/oauth2/token", body, ct).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (response.IsSuccessStatusCode)
        {
            return ReadTokens(root);
        }

        var error = root.TryGetProperty("message", out var msg) ? msg.GetString() : null;
        error ??= root.TryGetProperty("error", out var err) ? err.GetString() : null;
        if (string.Equals(error, "authorization_pending", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(error, "slow_down", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (string.Equals(error, "expired_token", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(error, "access_denied", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("El código de Twitch caducó o se canceló. Vuelve a intentarlo.");
        }

        throw new InvalidOperationException(TwitchApi.ReadHelixError(root, "Twitch no devolvió el acceso."));
    }

    public async Task<TwitchTokenSet> WaitForDeviceTokenAsync(
        string clientId,
        TwitchDeviceStart start,
        CancellationToken ct)
    {
        var interval = Math.Max(3, start.IntervalSeconds);
        var deadline = DateTime.UtcNow.AddSeconds(Math.Max(60, start.ExpiresInSeconds));
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var tokens = await PollDeviceTokenAsync(clientId, start.DeviceCode, ct).ConfigureAwait(false);
            if (tokens != null)
            {
                return tokens;
            }

            await Task.Delay(TimeSpan.FromSeconds(interval), ct).ConfigureAwait(false);
        }

        throw new TimeoutException("Se acabó el tiempo para autorizar Twitch.");
    }

    public async Task<TwitchTokenSet> RefreshAsync(string clientId, string refreshToken, CancellationToken ct)
    {
        using var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        });
        using var response = await _http.PostAsync("https://id.twitch.tv/oauth2/token", body, ct).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("La sesión de Twitch caducó. Vuelve a entrar con tu cuenta.");
        }

        return ReadTokens(doc.RootElement);
    }

    public async Task<TwitchUser> GetUserAsync(string clientId, string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.twitch.tv/helix/users");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("Client-Id", clientId);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(TwitchApi.ReadHelixError(doc.RootElement, "No se pudo leer tu usuario de Twitch."));
        }

        var data = doc.RootElement.GetProperty("data");
        if (data.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Twitch no devolvió el usuario.");
        }

        var user = data[0];
        return new TwitchUser(
            user.GetProperty("id").GetString() ?? "",
            user.GetProperty("login").GetString() ?? "",
            user.TryGetProperty("display_name", out var dn) ? dn.GetString() ?? "" : "");
    }

    private static TwitchTokenSet ReadTokens(JsonElement root)
    {
        var access = root.GetProperty("access_token").GetString() ?? "";
        var refresh = root.TryGetProperty("refresh_token", out var r) ? r.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(access))
        {
            throw new InvalidOperationException("Twitch no envió el token.");
        }

        return new TwitchTokenSet(access, refresh);
    }

    public void Dispose() => _http.Dispose();
}

public static class TwitchApi
{
    public static string ReadHelixError(JsonElement root, string fallback)
    {
        if (root.TryGetProperty("message", out var message) && message.GetString() is { Length: > 0 } text)
        {
            return text;
        }

        BridgeLog.Warn("Twitch API: respuesta de error sin message.");
        return fallback;
    }
}
