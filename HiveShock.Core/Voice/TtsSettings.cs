using System.Text.Json.Serialization;
using HiveShock.Configuration;

namespace HiveShock.Voice;

/// <summary>Motor de voz elegido por el streamer.</summary>
public static class TtsEngines
{
    /// <summary>Voces neurales de Microsoft Edge (en línea, más naturales).</summary>
    public const string Edge = "edge";

    /// <summary>Voces instaladas en Windows (sin internet).</summary>
    public const string Windows = "windows";

    /// <summary>Voces neurales locales de Piper (sin internet, se descargan aparte).</summary>
    public const string Piper = "piper";
}

/// <summary>Quién puede ser leído en voz alta.</summary>
public enum TtsAudience
{
    Everyone,
    SubscribersAndMods,
    ModsOnly,
}

/// <summary>
/// Config de Smart TTS. Vive en Core (no en HiveShock.Avalonia) porque el propio
/// SmartVoiceManager es de vida larga en BridgeRuntime, igual que UiPreferences es
/// para el resto de la UI de escritorio. Campos nuevos siempre con default: los
/// .hiveshock-tts.json viejos siguen cargando.
/// </summary>
public sealed class TtsSettings
{
    public static readonly string[] DefaultIgnoredUsers =
        ["nightbot", "streamelements", "streamlabs", "moobot", "fossabot", "wizebot", "sery_bot", "botrix"];

    public bool Enabled { get; set; }

    // --- Voz ---
    public string Engine { get; set; } = TtsEngines.Edge;

    /// <summary>Voz elegida en cada motor (id de motor → id de voz de ese motor).</summary>
    public Dictionary<string, string> VoiceIds
    {
        get => _voiceIds;
        // El deserializador crea un diccionario que distingue mayúsculas: se envuelve siempre.
        set => _voiceIds = new Dictionary<string, string>(value ?? [], StringComparer.OrdinalIgnoreCase);
    }

    private Dictionary<string, string> _voiceIds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Usar una voz distinta para cada plataforma (TikTok / Twitch) en vez de la misma para todo.</summary>
    public bool PerPlatformVoice { get; set; }

    /// <summary>Voz por motor y plataforma: clave "motor:plataforma" (p. ej. "edge:twitch") → id de voz.</summary>
    public Dictionary<string, string> PlatformVoiceIds
    {
        get => _platformVoiceIds;
        set => _platformVoiceIds = new Dictionary<string, string>(value ?? [], StringComparer.OrdinalIgnoreCase);
    }

    private Dictionary<string, string> _platformVoiceIds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Si el motor elegido falla (sin internet, bloqueado), leer ese mensaje con el siguiente
    /// motor disponible en vez de quedarse en silencio.
    /// </summary>
    public bool UseFallbackEngines { get; set; } = true;

    /// <summary>Solo lectura de archivos viejos (voz de Edge): se migra a <see cref="VoiceIds"/> y no se vuelve a escribir.</summary>
    [JsonPropertyName("VoiceId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyEdgeVoiceId { get => null; set => MigrateLegacyVoice(TtsEngines.Edge, value); }

    /// <summary>Solo lectura de archivos viejos (voz de Windows); ver <see cref="LegacyEdgeVoiceId"/>.</summary>
    [JsonPropertyName("WindowsVoiceId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyWindowsVoiceId { get => null; set => MigrateLegacyVoice(TtsEngines.Windows, value); }
    public double Volume { get; set; } = 1.0;
    public int RatePercent { get; set; }
    public int PitchPercent { get; set; }
    public string OutputDeviceId { get; set; } = "";

    // --- Voz aleatoria ---
    /// <summary>Cada mensaje con una voz al azar de los idiomas marcados (en vez de la voz fija).</summary>
    public bool RandomVoice { get; set; }

    /// <summary>Con voz aleatoria: cada persona del chat conserva siempre la misma voz.</summary>
    public bool RandomVoicePerUser { get; set; } = true;

    /// <summary>Códigos de idioma ("es", "en"…) cuyas voces entran en el sorteo.</summary>
    public List<string> RandomLanguages { get; set; } = ["es"];

    /// <summary>Voces desmarcadas a mano dentro de un idioma marcado (ids del motor).</summary>
    public List<string> RandomExcludedVoices { get; set; } = [];

    // --- Micrófono ---
    public bool PauseWhenISpeak { get; set; } = true;

    /// <summary>Si una frase se corta porque el streamer habló, repetirla entera cuando termine.</summary>
    public bool ReplayAfterInterruption { get; set; } = true;
    public string MicrophoneDeviceId { get; set; } = "";

    /// <summary>0..100. Más alto = detecta voz más baja (y también más ruido).</summary>
    public int MicSensitivity { get; set; } = 80;

    // --- Qué se lee ---
    public bool ReadTikTok { get; set; } = true;
    public bool ReadTwitch { get; set; } = true;
    public bool SayUserName { get; set; } = true;
    public bool SayPlatform { get; set; }
    public bool ReadGifts { get; set; } = true;
    public int GiftMinDiamonds { get; set; } = 1;
    public bool ReadFollows { get; set; }
    public bool ReadBits { get; set; } = true;
    public TtsAudience Audience { get; set; } = TtsAudience.Everyone;

    // --- Filtros ---
    public bool IgnoreOwnMessages { get; set; } = true;
    public bool SkipCommands { get; set; } = true;

    /// <summary>No leer mensajes que etiquetan a alguien con @usuario (suelen ser conversaciones entre viewers).</summary>
    public bool SkipMentions { get; set; }
    public bool RemoveLinks { get; set; } = true;
    public bool ReadEmojis { get; set; }
    public int MaxMessageLength { get; set; } = 150;
    public int MaxMessageAgeSeconds { get; set; } = 45;
    public int PerUserCooldownSeconds { get; set; } = 8;
    public int MaxQueueLength { get; set; } = 20;
    public List<string> IgnoredUsers { get; set; } = [.. DefaultIgnoredUsers];
    public List<string> BlockedWords { get; set; } = [];

    // --- Atajos ---
    public string MuteHotkey { get; set; } = "Control+Alt+M";
    public string SkipHotkey { get; set; } = "Control+Alt+N";

    private const string FileName = ".hiveshock-tts.json";

    public static TtsSettings Load()
    {
        var settings = UserDataStore.Load<TtsSettings>(FileName) ?? new TtsSettings();
        settings.Normalize();
        return settings;
    }

    /// <summary>Encierra valores editados a mano en el JSON dentro de rangos que la UI sabe mostrar.</summary>
    public void Normalize()
    {
        // Un motor desconocido se resuelve al predeterminado en TtsEngineRegistry.Resolve, no aquí:
        // así una config hecha con Piper no se pierde si se abre en una versión sin Piper.
        Engine = string.IsNullOrWhiteSpace(Engine) ? TtsEngines.Edge : Engine.Trim().ToLowerInvariant();
        VoiceIds = new Dictionary<string, string>(
            (VoiceIds ?? []).Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value)),
            StringComparer.OrdinalIgnoreCase);
        PlatformVoiceIds = new Dictionary<string, string>(
            (PlatformVoiceIds ?? []).Where(kv => kv.Key.Contains(':') && !string.IsNullOrWhiteSpace(kv.Value)),
            StringComparer.OrdinalIgnoreCase);
        OutputDeviceId ??= "";
        MicrophoneDeviceId ??= "";
        MuteHotkey ??= "";
        SkipHotkey ??= "";
        Volume = Math.Clamp(Volume, 0, 1);
        RatePercent = Math.Clamp(RatePercent, -50, 100);
        PitchPercent = Math.Clamp(PitchPercent, -50, 50);
        MicSensitivity = Math.Clamp(MicSensitivity, 0, 100);
        GiftMinDiamonds = Math.Max(1, GiftMinDiamonds);
        MaxMessageLength = Math.Clamp(MaxMessageLength, 20, 500);
        MaxMessageAgeSeconds = Math.Clamp(MaxMessageAgeSeconds, 5, 600);
        PerUserCooldownSeconds = Math.Clamp(PerUserCooldownSeconds, 0, 300);
        MaxQueueLength = Math.Clamp(MaxQueueLength, 1, 100);
        RandomLanguages = (RandomLanguages ?? []).Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Trim().ToLowerInvariant()).Distinct().ToList();
        RandomExcludedVoices = (RandomExcludedVoices ?? []).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().ToList();
        IgnoredUsers = (IgnoredUsers ?? []).Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u.Trim()).ToList();
        BlockedWords = (BlockedWords ?? []).Where(w => !string.IsNullOrWhiteSpace(w)).Select(w => w.Trim()).ToList();
    }

    /// <summary>Voz guardada para ese motor, o vacío (el motor usa su predeterminada).</summary>
    public string GetVoiceId(string engineId) => VoiceIds.TryGetValue(engineId, out var id) ? id : "";

    public void SetVoiceId(string engineId, string voiceId) => VoiceIds[engineId] = voiceId ?? "";

    /// <summary>Voz de ese motor para esa plataforma, o vacío (se usa la voz general del motor).</summary>
    public string GetPlatformVoiceId(string engineId, string portId) =>
        PlatformVoiceIds.TryGetValue($"{engineId}:{portId}", out var id) ? id : "";

    public void SetPlatformVoiceId(string engineId, string portId, string voiceId) =>
        PlatformVoiceIds[$"{engineId}:{portId}"] = voiceId ?? "";

    private void MigrateLegacyVoice(string engineId, string? voiceId)
    {
        if (!string.IsNullOrWhiteSpace(voiceId))
        {
            VoiceIds.TryAdd(engineId, voiceId);
        }
    }

    /// <summary>Guarda en la carpeta del usuario y junto al .exe, con respaldo .bak. Nunca lanza.</summary>
    public void Save() => UserDataStore.Save(FileName, this);
}
