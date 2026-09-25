using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using HiveShock.Logging;
using HiveShock.Voice.Piper.Runtime;

namespace HiveShock.Voice.Piper.Voices;

/// <summary>
/// Una voz disponible en disco: descargada del catálogo (carpeta con voice.json) o añadida a
/// mano por el streamer (<see cref="IsCustom"/>: un par nombre.onnx + nombre.onnx.json).
/// </summary>
public sealed record PiperInstalledVoice(PiperVoiceInfo Info, string ModelPath, string ConfigPath, bool IsCustom);

/// <summary>
/// Voces en disco (voices/). Las del catálogo se descargan en una carpeta temporal, se
/// verifican archivo por archivo (MD5) y solo entonces se mueven a voices/&lt;clave&gt;/. Además
/// se reconocen voces propias: cualquier "nombre.onnx" con su "nombre.onnx.json" copiados a
/// voices/ o a una subcarpeta; idioma, calidad y hablantes se leen de ese .onnx.json.
/// </summary>
public sealed partial class PiperVoiceStore
{
    private const string InfoFile = "voice.json";

    private readonly PiperPaths _paths;
    private readonly HttpClient _http;
    private readonly object _gate = new();
    private IReadOnlyList<PiperInstalledVoice>? _installed;

    public PiperVoiceStore(PiperPaths paths, HttpClient? http = null)
    {
        _paths = paths;
        _http = http ?? HttpDownloader.SharedClient;
    }

    /// <summary>Se dispara al instalar o borrar una voz.</summary>
    public event Action? Changed;

    /// <summary>Claves como "es_MX-claude-high" o "pt_PT-tugão-medium": letras, números, _ y -; nunca rutas (evita escribir fuera de la carpeta de voces).</summary>
    public static bool IsSafeKey(string key) => SafeKeyRegex().IsMatch(key);

    public IReadOnlyList<PiperInstalledVoice> Installed()
    {
        lock (_gate)
        {
            return _installed ??= Scan();
        }
    }

    public PiperInstalledVoice? Find(string key) =>
        Installed().FirstOrDefault(v => string.Equals(v.Info.Key, key, StringComparison.OrdinalIgnoreCase));

    public bool IsInstalled(string key) => Find(key) != null;

    public long TotalBytes() => Installed().Sum(v => v.Info.SizeBytes);

    public async Task InstallAsync(PiperVoiceInfo voice, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        if (!IsSafeKey(voice.Key))
        {
            throw new ArgumentException($"Clave de voz no válida: {voice.Key}", nameof(voice));
        }

        var target = Path.Combine(_paths.VoicesDirectory, voice.Key);
        var staging = target + ".download";
        if (Directory.Exists(staging))
        {
            Directory.Delete(staging, recursive: true);
        }

        Directory.CreateDirectory(staging);
        try
        {
            long done = 0;
            foreach (var file in voice.Files)
            {
                var local = Path.Combine(staging, Path.GetFileName(file.Path));
                await HttpDownloader.DownloadVerifiedAsync(_http, PiperVoiceCatalog.FileUrl(file), local, file.Md5,
                    HashAlgorithmName.MD5, progress, done, voice.SizeBytes, ct).ConfigureAwait(false);
                done += file.SizeBytes;
            }

            await File.WriteAllTextAsync(Path.Combine(staging, InfoFile), JsonSerializer.Serialize(voice), ct)
                .ConfigureAwait(false);

            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }

            Directory.Move(staging, target);
            BridgeLog.Info($"Piper: voz {voice.Key} instalada ({voice.SizeBytes / 1048576} MB)");
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                try
                {
                    Directory.Delete(staging, recursive: true);
                }
                catch
                {
                    // se limpia en el próximo intento
                }
            }
        }

        Invalidate();
    }

    public void Delete(string key)
    {
        var voice = Find(key);
        if (voice == null)
        {
            return;
        }

        if (voice.IsCustom)
        {
            // Solo sus dos archivos: la subcarpeta puede tener otras cosas del streamer.
            File.Delete(voice.ModelPath);
            File.Delete(voice.ConfigPath);
        }
        else if (IsSafeKey(key))
        {
            var dir = Path.Combine(_paths.VoicesDirectory, key);
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        Invalidate();
    }

    /// <summary>Vuelve a mirar la carpeta (tras copiar voces propias a mano).</summary>
    public void Refresh() => Invalidate();

    public void DeleteAll()
    {
        if (Directory.Exists(_paths.VoicesDirectory))
        {
            Directory.Delete(_paths.VoicesDirectory, recursive: true);
        }

        Invalidate();
    }

    private void Invalidate()
    {
        lock (_gate)
        {
            _installed = null;
        }

        Changed?.Invoke();
    }

    private List<PiperInstalledVoice> Scan()
    {
        var result = new List<PiperInstalledVoice>();
        if (!Directory.Exists(_paths.VoicesDirectory))
        {
            return result;
        }

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in Directory.EnumerateDirectories(_paths.VoicesDirectory))
        {
            if (dir.EndsWith(".download", StringComparison.OrdinalIgnoreCase))
            {
                continue; // descarga a medias
            }

            var infoPath = Path.Combine(dir, InfoFile);
            if (!File.Exists(infoPath))
            {
                AddCustomVoices(dir, result, keys); // subcarpeta con voces propias
                continue;
            }

            try
            {
                var info = JsonSerializer.Deserialize<PiperVoiceInfo>(File.ReadAllText(infoPath));
                if (info?.Model == null || info.Config == null)
                {
                    continue;
                }

                var model = Path.Combine(dir, Path.GetFileName(info.Model.Path));
                var config = Path.Combine(dir, Path.GetFileName(info.Config.Path));
                if (File.Exists(model) && File.Exists(config) && keys.Add(info.Key))
                {
                    result.Add(new PiperInstalledVoice(info, model, config, IsCustom: false));
                }
            }
            catch (Exception ex)
            {
                BridgeLog.Warn($"Piper: se ignora la voz en {Path.GetFileName(dir)}: {ex.Message}");
            }
        }

        AddCustomVoices(_paths.VoicesDirectory, result, keys); // voces propias sueltas en voices/

        // Orden estable (el sistema de archivos no garantiza ninguno): por clave.
        result.Sort((a, b) => string.Compare(a.Info.Key, b.Info.Key, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    /// <summary>Cada "nombre.onnx" con su "nombre.onnx.json" al lado es una voz propia.</summary>
    private static void AddCustomVoices(string dir, List<PiperInstalledVoice> result, HashSet<string> keys)
    {
        foreach (var model in Directory.EnumerateFiles(dir, "*.onnx"))
        {
            var config = model + ".json";
            if (!File.Exists(config))
            {
                BridgeLog.Warn($"Piper: la voz {Path.GetFileName(model)} no tiene su archivo {Path.GetFileName(config)} al lado.");
                continue;
            }

            try
            {
                var info = ReadCustomInfo(model, config);
                if (keys.Add(info.Key))
                {
                    result.Add(new PiperInstalledVoice(info, model, config, IsCustom: true));
                }
            }
            catch (Exception ex)
            {
                BridgeLog.Warn($"Piper: no se pudo leer la voz propia {Path.GetFileName(model)}: {ex.Message}");
            }
        }
    }

    /// <summary>Ficha de una voz propia a partir de su .onnx.json (mismo formato que las oficiales).</summary>
    internal static PiperVoiceInfo ReadCustomInfo(string modelPath, string configPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
        var root = doc.RootElement;

        var language = "";
        if (root.TryGetProperty("language", out var lang) && lang.TryGetProperty("code", out var code))
        {
            language = code.GetString() ?? "";
        }
        else if (root.TryGetProperty("espeak", out var espeak) && espeak.TryGetProperty("voice", out var v))
        {
            language = (v.GetString() ?? "").Replace('-', '_'); // "es-419" → "es_419"
        }

        var quality = root.TryGetProperty("audio", out var audio) && audio.TryGetProperty("quality", out var q)
            ? q.GetString() ?? ""
            : "";
        var speakers = root.TryGetProperty("speaker_id_map", out var map) && map.ValueKind == JsonValueKind.Object
            ? map.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt32())
            : new Dictionary<string, int>();
        var numSpeakers = root.TryGetProperty("num_speakers", out var n) && n.TryGetInt32(out var count) ? count : 1;

        // '#' separa el hablante en los ids de voz: no puede ir en la clave.
        var name = Path.GetFileNameWithoutExtension(modelPath);
        var key = name.Replace('#', '-');
        return new PiperVoiceInfo(key, name, language, quality, numSpeakers, speakers,
        [
            new PiperVoiceFile(Path.GetFileName(modelPath), new FileInfo(modelPath).Length, ""),
            new PiperVoiceFile(Path.GetFileName(configPath), new FileInfo(configPath).Length, ""),
        ]);
    }

    [GeneratedRegex(@"^[\p{L}\p{N}][\p{L}\p{N}_\-]{0,99}$")]
    private static partial Regex SafeKeyRegex();
}
