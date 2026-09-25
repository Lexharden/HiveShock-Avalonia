using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using HiveShock.Logging;
using HiveShock.Voice.Piper.Runtime;

namespace HiveShock.Voice.Piper.Voices;

/// <summary>Un archivo de una voz en el repositorio oficial (ruta relativa, tamaño, MD5).</summary>
public sealed record PiperVoiceFile(string Path, long SizeBytes, string Md5);

/// <summary>Una voz del catálogo oficial (huggingface.co/rhasspy/piper-voices).</summary>
public sealed record PiperVoiceInfo(
    string Key,
    string Name,
    string LanguageCode,
    string Quality,
    int NumSpeakers,
    IReadOnlyDictionary<string, int> Speakers,
    IReadOnlyList<PiperVoiceFile> Files)
{
    /// <summary>"es_MX" → "es-MX", el formato que usa el resto de HiveShock (agrupación, sorteo).</summary>
    [JsonIgnore]
    public string Locale => LanguageCode.Replace('_', '-');

    [JsonIgnore]
    public string DisplayName => Name.Length == 0 ? Key : char.ToUpper(Name[0], CultureInfo.InvariantCulture) + Name[1..].Replace('_', ' ');

    [JsonIgnore]
    public string QualityLabel => Quality switch
    {
        "x_low" => "calidad muy baja",
        "low" => "calidad baja",
        "medium" => "calidad media",
        "high" => "calidad alta",
        _ => Quality,
    };

    [JsonIgnore]
    public long SizeBytes => Files.Sum(f => f.SizeBytes);

    [JsonIgnore]
    public PiperVoiceFile? Model => Files.FirstOrDefault(f => f.Path.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase));

    [JsonIgnore]
    public PiperVoiceFile? Config => Files.FirstOrDefault(f => f.Path.EndsWith(".onnx.json", StringComparison.OrdinalIgnoreCase));

    /// <summary>Ficha de la voz con su licencia (cada voz tiene la suya; algunas no permiten uso comercial).</summary>
    [JsonIgnore]
    public string LicenseUrl => Files.FirstOrDefault(f => f.Path.EndsWith("MODEL_CARD", StringComparison.Ordinal)) is { } card
        ? $"https://huggingface.co/rhasspy/piper-voices/blob/main/{card.Path}"
        : "https://huggingface.co/rhasspy/piper-voices";
}

/// <summary>
/// Catálogo oficial de voces de Piper (voices.json). Se guarda una copia local para poder
/// listar y usar voces sin internet; se refresca como mucho una vez al día salvo que se pida.
/// </summary>
public sealed class PiperVoiceCatalog
{
    public const string RepositoryUrl = "https://huggingface.co/rhasspy/piper-voices/resolve/main/";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(1);

    private readonly PiperPaths _paths;
    private readonly HttpClient _http;

    public PiperVoiceCatalog(PiperPaths paths, HttpClient? http = null)
    {
        _paths = paths;
        _http = http ?? HttpDownloader.SharedClient;
    }

    public static string FileUrl(PiperVoiceFile file) => RepositoryUrl + file.Path;

    public async Task<IReadOnlyList<PiperVoiceInfo>> LoadAsync(bool forceRefresh, CancellationToken ct)
    {
        var cache = _paths.CatalogCachePath;
        var cacheFresh = File.Exists(cache) && DateTime.UtcNow - File.GetLastWriteTimeUtc(cache) < CacheLifetime;
        if (!forceRefresh && cacheFresh)
        {
            return Parse(await File.ReadAllTextAsync(cache, ct).ConfigureAwait(false));
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var json = await _http.GetStringAsync(RepositoryUrl + "voices.json", timeout.Token).ConfigureAwait(false);
            var voices = Parse(json); // valida antes de sobrescribir la copia buena
            Directory.CreateDirectory(_paths.Root);
            await File.WriteAllTextAsync(cache + ".tmp", json, ct).ConfigureAwait(false);
            File.Move(cache + ".tmp", cache, overwrite: true);
            return voices;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            if (File.Exists(cache))
            {
                BridgeLog.Warn($"Piper: catálogo sin conexión, se usa la copia local ({ex.Message})");
                return Parse(await File.ReadAllTextAsync(cache, ct).ConfigureAwait(false));
            }

            throw new InvalidOperationException(
                "No se pudo descargar la lista de voces locales. Revisa tu internet e inténtalo de nuevo.", ex);
        }
    }

    /// <summary>Lee voices.json. Ignora entradas incompletas en vez de fallar entero.</summary>
    public static IReadOnlyList<PiperVoiceInfo> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var voices = new List<PiperVoiceInfo>();
        foreach (var entry in doc.RootElement.EnumerateObject())
        {
            try
            {
                var el = entry.Value;
                var language = el.GetProperty("language");
                var files = el.GetProperty("files").EnumerateObject()
                    .Select(f => new PiperVoiceFile(
                        f.Name,
                        f.Value.GetProperty("size_bytes").GetInt64(),
                        f.Value.GetProperty("md5_digest").GetString() ?? ""))
                    .ToList();
                var speakers = el.TryGetProperty("speaker_id_map", out var map) && map.ValueKind == JsonValueKind.Object
                    ? map.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt32())
                    : new Dictionary<string, int>();

                var voice = new PiperVoiceInfo(
                    el.TryGetProperty("key", out var key) ? key.GetString() ?? entry.Name : entry.Name,
                    el.GetProperty("name").GetString() ?? "",
                    language.GetProperty("code").GetString() ?? "",
                    el.GetProperty("quality").GetString() ?? "",
                    el.TryGetProperty("num_speakers", out var n) ? n.GetInt32() : 1,
                    speakers,
                    files);
                if (PiperVoiceStore.IsSafeKey(voice.Key) && voice.Model != null && voice.Config != null)
                {
                    voices.Add(voice);
                }
            }
            catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException)
            {
                // entrada con formato inesperado: se salta
            }
        }

        return voices;
    }
}
