using System.Globalization;
using HiveShock.Input;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace HiveShock.Configuration;

public sealed class BridgeOptions
{
    public string TikTokUniqueId { get; set; } = "";
    public string? TikTokTtwid { get; set; }
    public string? TikTokCookies { get; set; }
    public string GameHost { get; set; } = "127.0.0.1";
    public int GamePort { get; set; } = 43000;
    public int TestPort { get; set; } = 43001;
    /// <summary>Puerto donde el bridge escucha eventos del juego (muertes, save borrado).</summary>
    public int EventPort { get; set; } = 43002;
    public bool DryRun { get; set; }
    public bool DisableTestServer { get; set; }
    public bool ListEffectsOnly { get; set; }
    public bool SkipMenu { get; set; }
    /// <summary>Solo pruebas / SDK → juego. No conecta a ningún canal.</summary>
    public bool DevMode { get; set; }
    /// <summary>Conecta a TikTok solo para capturar regalos al catálogo (sin efectos ni servidor de pruebas).</summary>
    public bool CaptureOnly { get; set; }
    public bool TikTokEnabled { get; set; } = true;
    /// <summary>Experimental: al reconectar a TikTok manda el último cursor leído por si reenvía lo perdido (HIVESHOCK_TIKTOK_RESUME=1).</summary>
    public bool TikTokResume { get; set; }
    public bool TwitchEnabled { get; set; } = true;
    public string TwitchClientId { get; set; } = "";
    public string? TwitchAccessToken { get; set; }
    public string? TwitchRefreshToken { get; set; }
    public string TwitchUserLogin { get; set; } = "";
    public string TwitchUserId { get; set; } = "";
    public TimeSpan LiveRetryInterval { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan GameConnectTimeout { get; set; } = TimeSpan.FromSeconds(3);
    /// <summary>Id de perfil preferido (--profile / HIVESHOCK_PROFILE). Vacío = active-profile.txt.</summary>
    public string? ProfileId { get; set; }
    /// <summary>Pausa mínima entre envíos TCP al juego (ms). 0 = sin pacing.</summary>
    public int EffectGapMs { get; set; } = 300;
    /// <summary>
    /// Cuánto insistir en entregar un efecto si el juego no está disponible (cerrado, cargando, reiniciando) antes
    /// de darlo por perdido. Cero = no reintentar. Los efectos esperan en orden: un regalo pagado no se pierde por
    /// un cierre breve del juego.
    /// </summary>
    public TimeSpan GameRetryWindow { get; set; } = TimeSpan.FromMinutes(10);

    public static BridgeOptions FromEnvironmentAndArgs(string[] args)
    {
        EnvFileLoader.Load(AppPaths.Find(".env"));

        var dry = HasFlag(args, "--dry") || EnvBool("TIKTOK_DRY_RUN");
        var noTest = HasFlag(args, "--no-test") || HasFlag(args, "--no-cc") || EnvBool("HIVESHOCK_NO_TEST") || EnvBool("DISABLE_CC");
        var list = HasFlag(args, "--list");
        var skipMenu = HasFlag(args, "--start") || HasFlag(args, "--no-menu");
        var dev = HasFlag(args, "--dev") || HasFlag(args, "--sdk") || EnvBool("BRIDGE_DEV_MODE");
        var tiktok = NormalizeUniqueId(ArgValue(args, "--tiktok") ?? Env("TIKTOK_UNIQUE_ID"));
        var profile = ArgValue(args, "--profile")
                      ?? EnvAny("HIVESHOCK_PROFILE", "CROWDBRIDGE_PROFILE")
                      ?? Env("BRIDGE_PROFILE");

        return new BridgeOptions
        {
            TikTokUniqueId = tiktok,
            TikTokTtwid = Env("TIKTOK_TTWID"),
            TikTokCookies = Env("TIKTOK_COOKIES"),
            TikTokEnabled = EnvFlag("TIKTOK_ENABLED", defaultValue: true),
            TikTokResume = EnvBool("HIVESHOCK_TIKTOK_RESUME"),
            TwitchEnabled = EnvFlag("TWITCH_ENABLED", defaultValue: true),
            TwitchClientId = Env("TWITCH_CLIENT_ID") ?? TwitchApp.ClientId,
            TwitchAccessToken = Env("TWITCH_ACCESS_TOKEN"),
            TwitchRefreshToken = Env("TWITCH_REFRESH_TOKEN"),
            TwitchUserLogin = Env("TWITCH_USER_LOGIN") ?? "",
            TwitchUserId = Env("TWITCH_USER_ID") ?? "",
            GameHost = Env("GAME_HOST") ?? "127.0.0.1",
            GamePort = EnvInt("GAME_PORT", 43000),
            TestPort = int.TryParse(EnvAny("HIVESHOCK_TEST_PORT", "CC_PORT"), out var testPort) ? testPort : 43001,
            EventPort = EnvInt("EVENT_PORT", 43002),
            DryRun = dry,
            DisableTestServer = noTest,
            ListEffectsOnly = list,
            SkipMenu = skipMenu,
            DevMode = dev,
            ProfileId = string.IsNullOrWhiteSpace(profile) ? null : profile.Trim(),
            EffectGapMs = Math.Clamp(EnvInt("EFFECT_GAP_MS", 300), 0, 5000),
            GameRetryWindow = TimeSpan.FromSeconds(Math.Clamp(EnvInt("GAME_RETRY_SECONDS", 600), 0, 3600)),
        };
    }

    public bool GameHostLooksLocal =>
        string.IsNullOrWhiteSpace(GameHost) ||
        GameHost.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
        GameHost.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        GameHost.Equals("::1", StringComparison.OrdinalIgnoreCase);

    /// <summary>Aplica host/puertos del perfil (sin pisar overrides explícitos de .env si ya diferían del default del perfil anterior).</summary>
    public void ApplyProfileEndpoints(GameProfileInfo profile, bool force = true)
    {
        // En Android no pises un host que el usuario ya puso (este teléfono o IP del PC).
        if (!OperatingSystem.IsAndroid() && (force || string.IsNullOrWhiteSpace(GameHost)))
        {
            GameHost = string.IsNullOrWhiteSpace(profile.GameHost) ? "127.0.0.1" : profile.GameHost;
        }

        if (force)
        {
            GamePort = profile.GamePort > 0 ? profile.GamePort : 43000;
            TestPort = profile.TestPort > 0 ? profile.TestPort : 43001;
            EventPort = profile.EventPort > 0 ? profile.EventPort : 43002;
        }
    }

    public static string NormalizeUniqueId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var text = value.Trim();
        text = Regex.Replace(text, @"^https?://(www\.)?tiktok\.com/@", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"/live/?$", "", RegexOptions.IgnoreCase);
        text = text.TrimStart('@');
        var cut = text.IndexOfAny(['/', '?', '#']);
        if (cut >= 0)
        {
            text = text[..cut];
        }

        return text.Trim();
    }

    public bool TikTokReady =>
        TikTokEnabled && !string.IsNullOrWhiteSpace(TikTokUniqueId);

    public bool TwitchReady =>
        TwitchEnabled &&
        !string.IsNullOrWhiteSpace(TwitchClientId) &&
        !string.IsNullOrWhiteSpace(TwitchAccessToken);

    public bool HasAnyLivePort => TikTokReady || TwitchReady;

    public string ResolvedTwitchClientId()
    {
        if (!string.IsNullOrWhiteSpace(TwitchClientId))
        {
            return TwitchClientId.Trim();
        }

        return TwitchApp.ClientId;
    }

    private static bool HasFlag(string[] args, string flag) =>
        args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

    private static string? ArgValue(string[] args, string flag)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static string? Env(string key)
    {
        var value = Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>Primera variable de entorno con valor. Las últimas son nombres antiguos que se siguen aceptando.</summary>
    public static string? EnvAny(params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = Env(key);
            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }

    private static int EnvInt(string key, int fallback) =>
        int.TryParse(Env(key), out var n) ? n : fallback;

    private static bool EnvFlag(string key, bool defaultValue)
    {
        var value = Env(key);
        if (value is null)
        {
            return defaultValue;
        }

        return value is "1" or "true" or "TRUE" or "yes" or "YES";
    }

    private static bool EnvBool(string key)
    {
        var value = Env(key);
        return value is "1" or "true" or "TRUE" or "yes" or "YES";
    }
}
