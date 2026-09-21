using HiveShock.Configuration;
using HiveShock.Logging;
using Microsoft.Extensions.Hosting;

namespace HiveShock.Live;

public sealed class TwitchLiveHostedService : BackgroundService, ILivePort
{
    private readonly BridgeOptions _options;
    private readonly LiveEffectRouter _router;
    private readonly LivePortHub _hub;

    public TwitchLiveHostedService(BridgeOptions options, LiveEffectRouter router, LivePortHub hub)
    {
        _options = options;
        _router = router;
        _hub = hub;
    }

    public string Id => LivePortIds.Twitch;
    public string DisplayName => "Twitch";
    public bool IsEnabled => _options.TwitchEnabled;
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.TwitchClientId) &&
        !string.IsNullOrWhiteSpace(_options.TwitchAccessToken);
    public LivePortCapability Capabilities =>
        LivePortCapability.Chat | LivePortCapability.Follow | LivePortCapability.Bits;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => RunAsync(stoppingToken);

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        if (_options.DevMode || _options.CaptureOnly || !_options.TwitchReady)
        {
            return;
        }

        _hub.Set(LivePortIds.Twitch, "Twitch", LivePortStatus.Connecting, "");
        BridgeLog.Info($"Twitch conectando @{_options.TwitchUserLogin}");

        var backoff = new ReconnectBackoff();
        while (!stoppingToken.IsCancellationRequested)
        {
            var reachedLive = false;
            try
            {
                await ConnectOnceAsync(stoppingToken, () => reachedLive = true).ConfigureAwait(false);
                _hub.Set(LivePortIds.Twitch, "Twitch", LivePortStatus.Connecting, "Reintentando…");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                // retry
            }
            catch (Exception ex)
            {
                BridgeLog.Warn($"Twitch: {ex.Message}");
                _hub.Set(LivePortIds.Twitch, "Twitch", LivePortStatus.Error, ex.Message);
            }

            var wait = backoff.NextDelay(reachedLive);
            BridgeLog.Warn($"Twitch desconectado. Reintento en {wait.TotalSeconds:0}s");
            try
            {
                await Task.Delay(wait, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _hub.Set(LivePortIds.Twitch, "Twitch", LivePortStatus.Off, "");
    }

    private async Task ConnectOnceAsync(CancellationToken ct, Action onLive)
    {
        var clientId = _options.ResolvedTwitchClientId();
        var access = _options.TwitchAccessToken ?? "";
        var refresh = _options.TwitchRefreshToken ?? "";
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(access))
        {
            throw new InvalidOperationException("Falta la sesión de Twitch.");
        }

        using var auth = new TwitchAuthClient();
        if (!string.IsNullOrWhiteSpace(refresh))
        {
            try
            {
                var tokens = await auth.RefreshAsync(clientId, refresh, ct).ConfigureAwait(false);
                access = tokens.AccessToken;
                refresh = string.IsNullOrWhiteSpace(tokens.RefreshToken) ? refresh : tokens.RefreshToken;
                TwitchSessionStore.Persist(_options, clientId, tokens.AccessToken, refresh, _options.TwitchUserLogin, _options.TwitchUserId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                BridgeLog.Warn($"Twitch token: {ex.Message}");
            }
        }

        var userId = _options.TwitchUserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            var user = await auth.GetUserAsync(clientId, access, ct).ConfigureAwait(false);
            userId = user.Id;
            TwitchSessionStore.Persist(_options, clientId, access, refresh, user.Login, user.Id);
        }

        var sub = new TwitchEventSubClient();
        await sub.RunAsync(
            clientId,
            access,
            userId,
            (chatter, chatterId, text) => _router.HandleChat(chatter, chatterId, text, ct, LivePortIds.Twitch),
            user => _router.HandleFollow(user, ct, LivePortIds.Twitch),
            (user, bits) => _router.HandleCheer(user, bits, ct),
            () =>
            {
                onLive();
                _hub.Set(LivePortIds.Twitch, "Twitch", LivePortStatus.Live, "");
                BridgeLog.Info($"Twitch conectado @{_options.TwitchUserLogin}");
            },
            ct).ConfigureAwait(false);
    }
}

public static class TwitchSessionStore
{
    public static void Persist(
        BridgeOptions options,
        string clientId,
        string accessToken,
        string refreshToken,
        string login,
        string userId)
    {
        options.TwitchClientId = clientId;
        options.TwitchAccessToken = accessToken;
        options.TwitchRefreshToken = refreshToken;
        options.TwitchUserLogin = login;
        options.TwitchUserId = userId;
        var path = EnvFileWriter.EnsureEnvPath();
        EnvFileWriter.Upsert(path, "TWITCH_CLIENT_ID", clientId);
        EnvFileWriter.Upsert(path, "TWITCH_ACCESS_TOKEN", accessToken);
        EnvFileWriter.Upsert(path, "TWITCH_REFRESH_TOKEN", refreshToken);
        EnvFileWriter.Upsert(path, "TWITCH_USER_LOGIN", login);
        EnvFileWriter.Upsert(path, "TWITCH_USER_ID", userId);
    }

    public static void Clear(BridgeOptions options)
    {
        options.TwitchAccessToken = "";
        options.TwitchRefreshToken = "";
        options.TwitchUserLogin = "";
        options.TwitchUserId = "";
        var path = EnvFileWriter.EnsureEnvPath();
        EnvFileWriter.Upsert(path, "TWITCH_ACCESS_TOKEN", "");
        EnvFileWriter.Upsert(path, "TWITCH_REFRESH_TOKEN", "");
        EnvFileWriter.Upsert(path, "TWITCH_USER_LOGIN", "");
        EnvFileWriter.Upsert(path, "TWITCH_USER_ID", "");
    }
}
