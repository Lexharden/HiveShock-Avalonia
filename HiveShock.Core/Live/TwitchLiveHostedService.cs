using HiveShock.Configuration;
using HiveShock.Logging;
using Microsoft.Extensions.Hosting;

namespace HiveShock.Live;

public sealed class TwitchLiveHostedService : BackgroundService, ILivePort
{
    private readonly BridgeOptions _options;
    private readonly LiveEffectRouter _router;
    private readonly LivePortHub _hub;
    private readonly ViewerGuard _guard;
    private readonly GiftConfigStore? _gifts;
    private readonly TwitchRewardManager? _rewards;
    private readonly ConnectionGap _gap = new();

    public TwitchLiveHostedService(
        BridgeOptions options,
        LiveEffectRouter router,
        LivePortHub hub,
        ViewerGuard guard,
        GiftConfigStore? gifts = null,
        TwitchRewardManager? rewards = null)
    {
        _options = options;
        _router = router;
        _hub = hub;
        _guard = guard;
        _gifts = gifts;
        _rewards = rewards;
    }

    public string Id => LivePortIds.Twitch;
    public string DisplayName => "Twitch";
    public bool IsEnabled => _options.TwitchEnabled;
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.TwitchClientId) &&
        !string.IsNullOrWhiteSpace(_options.TwitchAccessToken);
    public LivePortCapability Capabilities =>
        LivePortCapability.Chat | LivePortCapability.Follow | LivePortCapability.Bits | LivePortCapability.ChannelPoints;

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

            _gap.NoteDown();
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

    /// <summary>Tras un corte, pide a Twitch los seguidores nuevos de ese tramo (EventSub no reenvía lo perdido).</summary>
    private async Task RecoverFollowsAsync(string clientId, string accessToken, string userId, DateTimeOffset since, CancellationToken ct)
    {
        try
        {
            var followers = await TwitchApi.GetRecentFollowersAsync(clientId, accessToken, userId, ct).ConfigureAwait(false);
            var recovered = _router.RecoverTwitchFollows(followers, since, ct);
            BridgeLog.Info(recovered > 0
                ? $"Twitch: recuperados {recovered} seguidor(es) del corte."
                : "Twitch: no había seguidores nuevos del corte.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            BridgeLog.Warn($"Twitch: no se pudieron recuperar seguidores del corte ({ex.Message}).");
        }
    }

    /// <summary>
    /// Canjes que se quedaron pendientes en Twitch (cierre de la app, corte de red): EventSub no reenvía lo perdido,
    /// pero las recompensas de HiveShock no se saltan la cola, así que siguen ahí.
    /// </summary>
    private async Task RecoverRedemptionsAsync(TwitchRewardManager rewards, IReadOnlyList<string> active, CancellationToken ct)
    {
        try
        {
            var pending = await rewards.GetPendingAsync(active, ct).ConfigureAwait(false);
            if (pending.Count == 0)
            {
                return;
            }

            var launched = _router.RecoverTwitchRedemptions(pending, ct);
            BridgeLog.Info($"Twitch: {pending.Count} canje(s) pendiente(s) de antes de conectar; se lanzan {launched}.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            BridgeLog.Warn($"Twitch recompensas: no se pudieron recuperar los canjes pendientes ({ex.Message}).");
        }
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
                access = await TwitchTokenRefresher.RefreshAsync(_options, clientId, refresh, ct).ConfigureAwait(false);
                refresh = _options.TwitchRefreshToken ?? refresh;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                BridgeLog.Warn($"Twitch token: {ex.Message}");
            }
        }

        var redemptions = await CanUseRedemptionsAsync(access, ct).ConfigureAwait(false);
        if (redemptions && _rewards != null)
        {
            // Antes de escuchar: un canje que llegue nada más conectar ya sabe si la recompensa es de HiveShock.
            try
            {
                await _rewards.RefreshManageableAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                BridgeLog.Warn($"Twitch recompensas: no se pudieron leer las recompensas de HiveShock ({ex.Message}).");
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
            (viewer, text) =>
            {
                // Bloqueado: ni comandos.
                if (_guard.IsBlocked(viewer))
                {
                    return;
                }

                _router.HandleChat(viewer, text, ct);
            },
            viewer => _router.HandleFollow(viewer, ct),
            (viewer, bits) => _router.HandleCheer(viewer, bits, ct),
            () =>
            {
                onLive();
                var gap = _gap.NoteUp();
                var note = gap is { } g ? ConnectionGap.Describe("Twitch", g) : "";
                _hub.Set(LivePortIds.Twitch, "Twitch", LivePortStatus.Live, note);
                BridgeLog.Info($"Twitch conectado @{_options.TwitchUserLogin}");
                if (gap is { } cut)
                {
                    BridgeLog.Warn(note);
                    RecoverFollowsAsync(clientId, access, userId, DateTimeOffset.UtcNow - cut - TimeSpan.FromSeconds(30), ct).FireAndLog("Recuperar seguidores");
                }

                if (redemptions && _rewards is { HasManageable: true } rewards)
                {
                    var active = _gifts?.Snapshot.ActiveRewardIds() ?? [];
                    rewards.RequestSync(active, live: !_guard.IsPaused);
                    RecoverRedemptionsAsync(rewards, active, ct).FireAndLog("Recuperar canjes");
                }
            },
            ct,
            id => _router.Accept("tw", id, 0, TimeSpan.MaxValue, "Evento de Twitch"),
            redemptions ? (viewer, redemption) => _router.HandleTwitchRedemption(viewer, redemption, ct) : null)
            .ConfigureAwait(false);
    }

    private int _redemptionScopeWarned;

    /// <summary>
    /// Las cuentas enlazadas antes de las recompensas no tienen ese permiso (renovar el token no lo añade): sin él
    /// no se pide la suscripción y se avisa una vez de que hay que volver a entrar. Si no se puede comprobar, se intenta.
    /// </summary>
    private async Task<bool> CanUseRedemptionsAsync(string accessToken, CancellationToken ct)
    {
        IReadOnlyList<string>? scopes;
        try
        {
            scopes = await TwitchRewardsApi.GetScopesAsync(accessToken, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return true;
        }

        if (scopes == null || TwitchRewardsApi.HasRedemptionScope(scopes))
        {
            return true;
        }

        if (Interlocked.Exchange(ref _redemptionScopeWarned, 1) == 0)
        {
            BridgeLog.Warn(
                "Twitch recompensas: falta un permiso nuevo. Sal y vuelve a entrar con Twitch para usar los puntos del canal.");
        }

        return false;
    }
}

/// <summary>
/// Renueva el token de Twitch de uno en uno. En una app sin secreto (login por código) cada refresh token vale una
/// sola vez: si el puente y la pantalla de Twitch renovaran a la vez con el mismo, uno fallaría y la sesión se
/// perdería. Quien llega tarde ve que ya se renovó y usa el token nuevo.
/// </summary>
public static class TwitchTokenRefresher
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <param name="usedRefresh">El refresh token que leyó quien llama; si ya cambió, otro renovó antes.</param>
    public static async Task<string> RefreshAsync(
        BridgeOptions options,
        string clientId,
        string usedRefresh,
        CancellationToken ct)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var current = options.TwitchRefreshToken ?? "";
            if (!string.Equals(current, usedRefresh, StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(options.TwitchAccessToken))
            {
                return options.TwitchAccessToken;
            }

            using var auth = new TwitchAuthClient();
            var tokens = await auth.RefreshAsync(clientId, current, ct).ConfigureAwait(false);
            var refresh = string.IsNullOrWhiteSpace(tokens.RefreshToken) ? current : tokens.RefreshToken;
            TwitchSessionStore.Persist(
                options, clientId, tokens.AccessToken, refresh, options.TwitchUserLogin, options.TwitchUserId);
            return tokens.AccessToken;
        }
        finally
        {
            Gate.Release();
        }
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
