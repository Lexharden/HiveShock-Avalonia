using TikTokLive.Proto;

namespace HiveShock.Live;

/// <summary>
/// Quién disparó un evento del live. Junta lo que cada plataforma da de una persona: el id numérico
/// (no cambia nunca), el @usuario (se puede cambiar, y otra persona puede quedarse con el viejo) y el
/// nombre que se ve (cualquiera puede ponerse el mismo). Por eso el id manda siempre que exista y el
/// @usuario solo sirve cuando no hay id.
/// </summary>
public sealed record ViewerIdentity
{
    public ViewerIdentity(string portId, string? userId, string? handle, string? name)
    {
        PortId = portId ?? "";
        UserId = NormalizeId(userId);
        Handle = NormalizeHandle(handle);
        Name = (name ?? "").Trim();
    }

    public string PortId { get; }

    /// <summary>Id numérico de la plataforma; vacío si no llegó.</summary>
    public string UserId { get; }

    /// <summary>@usuario en minúsculas y sin la @; vacío si no llegó.</summary>
    public string Handle { get; }

    /// <summary>Nombre visible. No identifica a nadie: solo se usa para mostrarlo.</summary>
    public string Name { get; }

    public bool HasId => UserId.Length > 0;

    /// <summary>Hay algo con lo que reconocer a la persona (id o @usuario).</summary>
    public bool IsIdentified => HasId || Handle.Length > 0;

    /// <summary>Clave para limitar por persona (enfriamiento, voz): id, si no @usuario y, al final, el nombre.</summary>
    public string StableKey => HasId ? UserId : Handle.Length > 0 ? Handle : Name;

    /// <summary>Cómo nombrarlo en pantalla y en el log.</summary>
    public string Label =>
        Name.Length > 0 ? Name :
        Handle.Length > 0 ? "@" + Handle :
        HasId ? "id " + UserId :
        "anónimo";

    /// <summary>Nombre, @usuario e id, para que el streamer sepa de quién se trata.</summary>
    public string Detail
    {
        get
        {
            var parts = new List<string>();
            if (Handle.Length > 0)
            {
                parts.Add("@" + Handle);
            }

            if (HasId)
            {
                parts.Add("id " + UserId);
            }

            return parts.Count == 0
                ? Label
                : Name.Length > 0 && !string.Equals(Name, Handle, StringComparison.OrdinalIgnoreCase)
                    ? $"{Name} ({string.Join(" · ", parts)})"
                    : string.Join(" · ", parts);
        }
    }

    public static ViewerIdentity TikTok(UserIdentity? user) => new(
        LivePortIds.TikTok,
        user is { UserId: > 0 } ? user.UserId.ToString() : "",
        user?.UniqueId,
        user?.Nickname is { Length: > 0 } nick ? nick : user?.UniqueId);

    public static ViewerIdentity Twitch(string? userId, string? login, string? displayName) => new(
        LivePortIds.Twitch,
        userId,
        login,
        string.IsNullOrWhiteSpace(displayName) ? login : displayName);

    public static string NormalizeHandle(string? handle) =>
        (handle ?? "").Trim().TrimStart('@').Trim().ToLowerInvariant();

    private static string NormalizeId(string? id)
    {
        var text = (id ?? "").Trim();
        return text is "0" ? "" : text;
    }
}
