using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HiveShock.Configuration;
using HiveShock.Logging;

namespace HiveShock.Input;

/// <summary>Macros de un perfil (<c>keys.json</c>). Solo lectura una vez cargado; se reemplaza entero al recargar.</summary>
public sealed class KeyMacroCatalog
{
    public static readonly KeyMacroCatalog Empty = new([]);

    private readonly Dictionary<string, KeyMacro> _macros;

    public KeyMacroCatalog(IEnumerable<KeyMacro> macros)
    {
        _macros = new Dictionary<string, KeyMacro>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in macros)
        {
            if (!string.IsNullOrWhiteSpace(m.Id))
            {
                _macros[m.Id] = m;
            }
        }
    }

    public IReadOnlyCollection<KeyMacro> All => _macros.Values;

    public bool Contains(string id) => !string.IsNullOrWhiteSpace(id) && _macros.ContainsKey(id);

    public bool TryGet(string id, out KeyMacro macro)
    {
        macro = null!;
        return !string.IsNullOrWhiteSpace(id) && _macros.TryGetValue(id, out macro!);
    }
}

public static class KeyMacroCatalogStore
{
    public const string FileName = "keys.json";

    private static readonly JsonSerializerOptions Options = new(JsonDefaults.Options)
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private sealed class FileShape
    {
        public List<KeyMacro> Macros { get; set; } = [];
    }

    /// <summary>Sin archivo o con JSON roto devuelve un catálogo vacío (y avisa): nunca debe tumbar el arranque.</summary>
    public static KeyMacroCatalog Load(string path)
    {
        if (!File.Exists(path))
        {
            return KeyMacroCatalog.Empty;
        }

        try
        {
            var shape = JsonSerializer.Deserialize<FileShape>(File.ReadAllText(path, Encoding.UTF8), Options);
            return new KeyMacroCatalog(shape?.Macros ?? []);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            BridgeLog.Warn($"No se pudo leer {Path.GetFileName(path)}: {ex.Message}");
            return KeyMacroCatalog.Empty;
        }
    }

    public static void Save(string path, IEnumerable<KeyMacro> macros) =>
        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(new FileShape { Macros = macros.ToList() }, Options));
}
