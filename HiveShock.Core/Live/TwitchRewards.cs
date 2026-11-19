using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace HiveShock.Live;

/// <summary>Un canje de puntos del canal (EventSub <c>channel.channel_points_custom_reward_redemption.add</c>).</summary>
/// <param name="Id">Id del canje: único y estable, sirve para no lanzarlo dos veces.</param>
/// <param name="RewardId">Id de la recompensa: no cambia aunque se renombre en Twitch.</param>
public sealed record TwitchRedemption(string Id, string RewardId, string RewardTitle, int Cost, string UserInput);

/// <summary>Una recompensa personalizada del canal, tal como la devuelve Helix.</summary>
/// <param name="IsManageable">
/// La creó HiveShock (mismo Client ID): solo de estas se pueden devolver puntos, completar canjes y pausarlas.
/// </param>
public sealed record TwitchChannelReward(string Id, string Title, int Cost, bool IsEnabled, bool IsPaused)
{
    public bool IsManageable { get; init; }
    public string Prompt { get; init; } = "";
    public int GlobalCooldownSeconds { get; init; }
    public int MaxPerStream { get; init; }
    public int MaxPerUserPerStream { get; init; }

    /// <summary>Lo que se puede editar desde HiveShock, con los valores actuales.</summary>
    public TwitchRewardSpec ToSpec() =>
        new(Title, Cost, Prompt, GlobalCooldownSeconds, MaxPerStream, MaxPerUserPerStream);
}

/// <summary>
/// Datos de una recompensa que crea o edita HiveShock. 0 en esperas y límites = sin límite. El nombre debe ser único
/// en el canal (Twitch lo exige) y el coste al menos 1.
/// </summary>
public sealed record TwitchRewardSpec(
    string Title,
    int Cost,
    string Prompt = "",
    int GlobalCooldownSeconds = 0,
    int MaxPerStream = 0,
    int MaxPerUserPerStream = 0)
{
    public const int MaxTitleLength = 45;
    public const int MaxPromptLength = 200;

    /// <summary>Qué falta o sobra para que Twitch la acepte; null si vale.</summary>
    public string? Validate()
    {
        var title = (Title ?? "").Trim();
        if (title.Length == 0)
        {
            return "Ponle un nombre a la recompensa.";
        }

        if (title.Length > MaxTitleLength)
        {
            return $"El nombre puede tener como mucho {MaxTitleLength} caracteres.";
        }

        if (Cost < 1)
        {
            return "El coste debe ser de al menos 1 punto.";
        }

        if ((Prompt ?? "").Trim().Length > MaxPromptLength)
        {
            return $"La descripción puede tener como mucho {MaxPromptLength} caracteres.";
        }

        return GlobalCooldownSeconds < 0 || MaxPerStream < 0 || MaxPerUserPerStream < 0
            ? "Las esperas y los límites no pueden ser negativos (0 = sin límite)."
            : null;
    }

    /// <summary>
    /// Cuerpo para crear o editar en Helix. Los canjes NO se saltan la cola de Twitch: si se saltaran, salen ya
    /// completados y no habría forma de devolver los puntos cuando el efecto no llega al juego.
    /// </summary>
    public Dictionary<string, object> ToHelixBody() => new()
    {
        ["title"] = (Title ?? "").Trim(),
        ["cost"] = Cost,
        ["prompt"] = (Prompt ?? "").Trim(),
        ["is_global_cooldown_enabled"] = GlobalCooldownSeconds > 0,
        ["global_cooldown_seconds"] = Math.Max(1, GlobalCooldownSeconds),
        ["is_max_per_stream_enabled"] = MaxPerStream > 0,
        ["max_per_stream"] = Math.Max(1, MaxPerStream),
        ["is_max_per_user_per_stream_enabled"] = MaxPerUserPerStream > 0,
        ["max_per_user_per_stream"] = Math.Max(1, MaxPerUserPerStream),
        ["should_redemptions_skip_request_queue"] = false,
    };
}

/// <summary>Un canje que sigue pendiente en la cola de Twitch (para recuperarlo tras un corte).</summary>
public sealed record TwitchPendingRedemption(
    string UserId,
    string Login,
    string DisplayName,
    TwitchRedemption Redemption,
    DateTimeOffset RedeemedAt);

public enum TwitchRewardsAvailability
{
    Ok,

    /// <summary>No hay cuenta de Twitch enlazada (o la sesión caducó del todo).</summary>
    NotLoggedIn,

    /// <summary>La cuenta se enlazó con una versión que no pedía el permiso de recompensas.</summary>
    NeedsRelogin,

    /// <summary>Los puntos del canal solo existen en canales Afiliado o Partner.</summary>
    NotAffiliate,

    Error,
}

public sealed record TwitchRewardsResult(
    TwitchRewardsAvailability Availability,
    IReadOnlyList<TwitchChannelReward> Rewards,
    string Message)
{
    public static TwitchRewardsResult Fail(TwitchRewardsAvailability availability, string message) =>
        new(availability, [], message);
}

public static class TwitchRewardsApi
{
    /// <summary>
    /// Permisos que dejan leer los canjes. Se pide el de gestionar (incluye leer) para que la fase de recompensas
    /// gestionadas (devolver puntos, pausarlas) no obligue a volver a entrar otra vez.
    /// </summary>
    public const string ReadScope = "channel:read:redemptions";
    public const string ManageScope = "channel:manage:redemptions";

    public const string EventSubType = "channel.channel_points_custom_reward_redemption.add";

    public static bool HasRedemptionScope(IEnumerable<string> scopes) =>
        scopes.Any(s => string.Equals(s, ReadScope, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(s, ManageScope, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Permisos del token (<c>GET /oauth2/validate</c>). Null si el token ya no vale (caducó o se revocó).
    /// </summary>
    public static async Task<IReadOnlyList<string>?> GetScopesAsync(
        string accessToken,
        CancellationToken ct,
        HttpClient? http = null)
    {
        http ??= HiveShock.Net.SharedHttp.Client;
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://id.twitch.tv/oauth2/validate");
        request.Headers.Authorization = new AuthenticationHeaderValue("OAuth", accessToken);
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(TwitchApi.ReadHelixError(doc.RootElement, "Twitch no validó la sesión."));
        }

        return ParseScopes(doc.RootElement);
    }

    public static IReadOnlyList<string> ParseScopes(JsonElement root) =>
        root.TryGetProperty("scopes", out var scopes) && scopes.ValueKind == JsonValueKind.Array
            ? [.. scopes.EnumerateArray().Where(s => s.ValueKind == JsonValueKind.String).Select(s => s.GetString() ?? "")]
            : [];

    /// <summary>Recompensas personalizadas del canal (todas, también las creadas desde el panel de Twitch).</summary>
    public static async Task<TwitchRewardsResult> GetCustomRewardsAsync(
        string clientId,
        string accessToken,
        string broadcasterId,
        CancellationToken ct,
        HttpClient? http = null,
        bool onlyManageable = false)
    {
        http ??= HiveShock.Net.SharedHttp.Client;
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.twitch.tv/helix/channel_points/custom_rewards?broadcaster_id={Uri.EscapeDataString(broadcasterId)}" +
            (onlyManageable ? "&only_manageable_rewards=true" : ""));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("Client-Id", clientId);
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        if (response.IsSuccessStatusCode)
        {
            var rewards = ParseRewards(doc.RootElement);
            return new TwitchRewardsResult(
                TwitchRewardsAvailability.Ok,
                rewards,
                rewards.Count == 0 ? "Tu canal no tiene recompensas personalizadas. Créalas en el panel de Twitch." : "");
        }

        var helixMessage = TwitchApi.ReadHelixError(doc.RootElement, "Twitch no devolvió las recompensas.");

        // 403 «The broadcaster must have partner or affiliate status»: el canal no tiene puntos.
        if (response.StatusCode == HttpStatusCode.Forbidden &&
            (helixMessage.Contains("affiliate", StringComparison.OrdinalIgnoreCase) ||
             helixMessage.Contains("partner", StringComparison.OrdinalIgnoreCase)))
        {
            return TwitchRewardsResult.Fail(
                TwitchRewardsAvailability.NotAffiliate,
                "Los puntos del canal solo existen en canales Afiliado o Partner de Twitch.");
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return TwitchRewardsResult.Fail(
                TwitchRewardsAvailability.NeedsRelogin,
                "Twitch no deja leer las recompensas con esta sesión. Sal y vuelve a entrar con tu cuenta.");
        }

        return TwitchRewardsResult.Fail(TwitchRewardsAvailability.Error, helixMessage);
    }

    /// <param name="manageable">Ids de las recompensas creadas por HiveShock (se marcan con IsManageable).</param>
    public static IReadOnlyList<TwitchChannelReward> ParseRewards(JsonElement root, IReadOnlySet<string>? manageable = null)
    {
        var list = new List<TwitchChannelReward>();
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var item in data.EnumerateArray())
        {
            var id = Str(item, "id");
            var title = Str(item, "title");
            if (id.Length == 0 || title.Length == 0)
            {
                continue;
            }

            list.Add(new TwitchChannelReward(
                id,
                title,
                Int(item, "cost"),
                !item.TryGetProperty("is_enabled", out var enabled) || enabled.ValueKind != JsonValueKind.False,
                item.TryGetProperty("is_paused", out var paused) && paused.ValueKind == JsonValueKind.True)
            {
                IsManageable = manageable?.Contains(id) == true,
                Prompt = Str(item, "prompt"),
                GlobalCooldownSeconds = Setting(item, "global_cooldown_setting", "global_cooldown_seconds"),
                MaxPerStream = Setting(item, "max_per_stream_setting", "max_per_stream"),
                MaxPerUserPerStream = Setting(item, "max_per_user_per_stream_setting", "max_per_user_per_stream"),
            });
        }

        return [.. list.OrderBy(r => r.Cost).ThenBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>Canjes de <c>GET /channel_points/custom_rewards/redemptions</c>. Se saltan los que no se pueden leer.</summary>
    public static IReadOnlyList<TwitchPendingRedemption> ParsePendingRedemptions(JsonElement root)
    {
        var list = new List<TwitchPendingRedemption>();
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var item in data.EnumerateArray())
        {
            if (ParseRedemption(item) is not { } redemption ||
                !DateTimeOffset.TryParse(Str(item, "redeemed_at"), System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal, out var at))
            {
                continue;
            }

            list.Add(new TwitchPendingRedemption(
                Str(item, "user_id"), Str(item, "user_login"), Str(item, "user_name"), redemption, at));
        }

        return list;
    }

    private static int Int(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? n : 0;

    /// <summary>Valor de un ajuste de Twitch («{ is_enabled, valor }»); 0 si está desactivado.</summary>
    private static int Setting(JsonElement item, string setting, string value) =>
        item.TryGetProperty(setting, out var s) && s.ValueKind == JsonValueKind.Object &&
        s.TryGetProperty("is_enabled", out var on) && on.ValueKind == JsonValueKind.True
            ? Int(s, value)
            : 0;

    /// <summary>Lee el canje de la notificación. Null si le falta el id del canje o de la recompensa.</summary>
    public static TwitchRedemption? ParseRedemption(JsonElement evt)
    {
        var id = Str(evt, "id");
        if (id.Length == 0 || !evt.TryGetProperty("reward", out var reward) || reward.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var rewardId = Str(reward, "id");
        if (rewardId.Length == 0)
        {
            return null;
        }

        return new TwitchRedemption(
            id,
            rewardId,
            Str(reward, "title"),
            reward.TryGetProperty("cost", out var cost) && cost.TryGetInt32(out var c) ? c : 0,
            Str(evt, "user_input"));
    }

    private static string Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
