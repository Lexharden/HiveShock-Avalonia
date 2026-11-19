using HiveShock.Configuration;
using HiveShock.Live;
using HiveShock.Logging;
using HiveShock.Platform;

namespace HiveShock.Hosting;

/// <summary>Ajustes de canales y conexión que se guardan en el .env: TikTok, Twitch, host del juego y modo ensayo.</summary>
public sealed partial class BridgeRuntime
{
    /// <summary>Olvida quién ya disparó el efecto de seguir. Solo con el puente detenido.</summary>
    public void ClearFollowHistory()
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("Detén el puente para borrar el historial de seguidores.");
        }

        FollowGate.ClearPersisted();
    }

    public void SaveGameHost(string host)
    {
        var value = host.Trim();
        if (string.IsNullOrEmpty(value))
        {
            value = "127.0.0.1";
        }

        Options.GameHost = value;
        EnvFileWriter.Upsert(EnvFileWriter.EnsureEnvPath(), "GAME_HOST", value);
    }

    public void SaveChannel(string uniqueId)
    {
        var normalized = BridgeOptions.NormalizeUniqueId(uniqueId);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Usuario inválido.");
        }

        Options.TikTokUniqueId = normalized;
        EnvFileWriter.Upsert(EnvFileWriter.EnsureEnvPath(), "TIKTOK_UNIQUE_ID", normalized);
    }

    public void SetTikTokEnabled(bool enabled)
    {
        Options.TikTokEnabled = enabled;
        EnvFileWriter.Upsert(EnvFileWriter.EnsureEnvPath(), "TIKTOK_ENABLED", enabled ? "1" : "0");
    }

    public void SetTwitchEnabled(bool enabled)
    {
        Options.TwitchEnabled = enabled;
        EnvFileWriter.Upsert(EnvFileWriter.EnsureEnvPath(), "TWITCH_ENABLED", enabled ? "1" : "0");
    }

    public void SaveTwitchClientId(string clientId)
    {
        Options.TwitchClientId = clientId.Trim();
        EnvFileWriter.Upsert(EnvFileWriter.EnsureEnvPath(), "TWITCH_CLIENT_ID", Options.TwitchClientId);
    }

    public async Task LoginTwitchAsync(IProgress<TwitchDeviceStart>? progress, CancellationToken ct)
    {
        var clientId = Options.ResolvedTwitchClientId();
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException("Twitch no está disponible en esta copia.");
        }

        using var auth = new TwitchAuthClient();
        var start = await auth.StartDeviceLoginAsync(clientId, ct).ConfigureAwait(false);
        progress?.Report(start);
        try
        {
            PlatformShell.OpenUrl(start.VerificationUri);
        }
        catch
        {
            // el usuario puede abrir la URL a mano
        }

        var tokens = await auth.WaitForDeviceTokenAsync(clientId, start, ct).ConfigureAwait(false);
        var user = await auth.GetUserAsync(clientId, tokens.AccessToken, ct).ConfigureAwait(false);
        TwitchSessionStore.Persist(Options, clientId, tokens.AccessToken, tokens.RefreshToken, user.Login, user.Id);
        BridgeLog.Info($"Twitch cuenta @{user.Login}");
        Options.TwitchEnabled = true;
        EnvFileWriter.Upsert(EnvFileWriter.EnsureEnvPath(), "TWITCH_ENABLED", "1");
    }

    public void LogoutTwitch()
    {
        TwitchSessionStore.Clear(Options);
        BridgeLog.Info("Twitch: sesión cerrada.");
    }

    public void SetDryRun(bool value) => Options.DryRun = value;

    /// <summary>
    /// Recompensas de puntos del canal, para elegirlas en la pantalla de Twitch. Nunca lanza: lo que impide usarlas
    /// (sin cuenta, falta volver a entrar, canal no Afiliado…) vuelve como <see cref="TwitchRewardsResult.Availability"/>.
    /// </summary>
    public async Task<TwitchRewardsResult> ListTwitchRewardsAsync(CancellationToken ct = default)
    {
        var clientId = Options.ResolvedTwitchClientId();
        var access = Options.TwitchAccessToken ?? "";
        var userId = Options.TwitchUserId ?? "";
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(access) || string.IsNullOrWhiteSpace(userId))
        {
            return TwitchRewardsResult.Fail(TwitchRewardsAvailability.NotLoggedIn, "Entra con tu cuenta de Twitch para ver tus recompensas.");
        }

        try
        {
            var scopes = await TwitchRewardsApi.GetScopesAsync(access, ct).ConfigureAwait(false);
            var refresh = Options.TwitchRefreshToken ?? "";
            if (scopes == null && !string.IsNullOrWhiteSpace(refresh))
            {
                // El token dura unas horas: con el puente parado nadie lo renovó.
                access = await TwitchTokenRefresher.RefreshAsync(Options, clientId, refresh, ct).ConfigureAwait(false);
                scopes = await TwitchRewardsApi.GetScopesAsync(access, ct).ConfigureAwait(false);
            }

            if (scopes == null)
            {
                return TwitchRewardsResult.Fail(
                    TwitchRewardsAvailability.NotLoggedIn, "La sesión de Twitch caducó. Sal y vuelve a entrar con tu cuenta.");
            }

            if (!TwitchRewardsApi.HasRedemptionScope(scopes))
            {
                return TwitchRewardsResult.Fail(
                    TwitchRewardsAvailability.NeedsRelogin,
                    "Para usar los puntos del canal, sal y vuelve a entrar con Twitch (hace falta un permiso nuevo).");
            }

            return await TwitchRewardsApi.GetCustomRewardsAsync(clientId, access, userId, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return TwitchRewardsResult.Fail(TwitchRewardsAvailability.Error, $"No se pudieron leer las recompensas: {ex.Message}");
        }
    }
}
