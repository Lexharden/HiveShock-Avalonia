using HiveShock.Configuration;

namespace HiveShock.Zeldathon;

/// <summary>
/// Conexión de este corredor con el servidor de Zeldathon. Se guarda con <see cref="UserDataStore"/>
/// (copia redundante + respaldo). El token identifica al corredor: nunca se escribe en logs.
/// </summary>
public sealed class ZeldathonSettings
{
    public const string FileName = ".hiveshock-zeldathon.json";

    public bool Enabled { get; set; }

    /// <summary>Dirección base del servidor: <c>https://zeldathon.ejemplo.com</c> (también vale solo el dominio).</summary>
    public string ServerUrl { get; set; } = "";

    public string Token { get; set; } = "";

    /// <summary>Conecta sola al abrir HiveShock si hay URL y token.</summary>
    public bool AutoConnect { get; set; } = true;

    /// <summary>Tiempo por donaciones: cuánto suman o restan los regalos de TikTok y los bits de Twitch.</summary>
    public DonationTimeSettings Donations { get; set; } = new();

    public static ZeldathonSettings Load() =>
        UserDataStore.Load<ZeldathonSettings>(FileName) ?? new ZeldathonSettings();

    /// <summary>Guarda en las dos ubicaciones con respaldo .bak. Nunca lanza.</summary>
    public void Save() => UserDataStore.Save(FileName, this);

    public bool HasCredentials =>
        !string.IsNullOrWhiteSpace(ServerUrl) && !string.IsNullOrWhiteSpace(Token);

    /// <summary>
    /// Construye la URL del WebSocket de ingesta (<c>wss://host/ingest</c>). Solo se admite cifrado
    /// (https/wss), salvo en la propia máquina (localhost) para desarrollo.
    /// </summary>
    public bool TryBuildIngestUri(out Uri uri, out string error)
    {
        uri = null!;
        error = "";
        var raw = (ServerUrl ?? "").Trim();
        if (raw.Length == 0)
        {
            error = "Falta la dirección del servidor.";
            return false;
        }

        if (!raw.Contains("://", StringComparison.Ordinal))
        {
            raw = "https://" + raw;
        }

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var parsed) || string.IsNullOrEmpty(parsed.Host))
        {
            error = "La dirección del servidor no es válida.";
            return false;
        }

        var secure = parsed.Scheme is "https" or "wss";
        var plain = parsed.Scheme is "http" or "ws";
        if (!secure && !plain)
        {
            error = "Usa una dirección https:// (o wss://).";
            return false;
        }

        if (plain && !parsed.IsLoopback)
        {
            error = "La conexión debe ser cifrada (https). Solo se permite http en esta misma máquina.";
            return false;
        }

        var builder = new UriBuilder(parsed)
        {
            Scheme = secure ? "wss" : "ws",
            Query = "",
            Fragment = "",
        };
        var path = builder.Path.TrimEnd('/');
        if (path.EndsWith("/ingest", StringComparison.OrdinalIgnoreCase))
        {
            path = path[..^"/ingest".Length];
        }

        builder.Path = path + "/ingest";
        uri = builder.Uri;
        return true;
    }

    /// <summary>Token enmascarado para mostrarlo en pantalla (nunca completo).</summary>
    public static string Mask(string? token)
    {
        var t = (token ?? "").Trim();
        if (t.Length == 0)
        {
            return "";
        }

        return t.Length <= 8 ? new string('•', t.Length) : $"{t[..4]}••••{t[^4..]}";
    }
}
