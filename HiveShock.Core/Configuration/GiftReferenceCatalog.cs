using System.Text;
using System.Text.Json;
using HiveShock.Logging;

namespace HiveShock.Configuration;

/// <summary>Un regalo de TikTok según el archivo de referencia (tiktok_gifts.json).</summary>
public sealed record GiftReferenceEntry(string Id, string Name, int? Diamonds, string IconUrl, string ImageFile);

/// <summary>
/// Lista oficial de regalos de TikTok que mantiene el desarrollador en <c>tiktok_gifts.json</c>
/// (junto a gift-catalog.json): id, nombre, diamantes e imagen en <c>gifts-images/</c>. Es solo
/// lectura: completa lo que el catálogo aprende del live (<see cref="GiftCatalogStore"/>) y
/// permite asignar efectos a regalos que todavía no se han visto en un directo.
/// Tolerante con el formato: lista o {"gifts": [...]}, claves en español o inglés, id como
/// número o texto; las entradas incompletas se saltan en vez de invalidar el archivo.
/// </summary>
public sealed class GiftReferenceCatalog
{
    public const string FileName = "tiktok_gifts.json";

    private readonly Dictionary<string, GiftReferenceEntry> _byId;
    private readonly Dictionary<string, GiftReferenceEntry> _byName;

    private GiftReferenceCatalog(IReadOnlyList<GiftReferenceEntry> entries, string? path)
    {
        All = entries;
        Path = path;
        _byId = new Dictionary<string, GiftReferenceEntry>(StringComparer.Ordinal);
        _byName = new Dictionary<string, GiftReferenceEntry>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            _byId.TryAdd(entry.Id, entry);
            // Hay nombres repetidos con otro id (versiones del mismo regalo): gana el primero.
            var key = NameKey(entry.Name);
            if (key.Length > 0)
            {
                _byName.TryAdd(key, entry);
            }
        }
    }

    public static GiftReferenceCatalog Empty { get; } = new([], null);

    /// <summary>Ruta del archivo leído, o null si no había.</summary>
    public string? Path { get; }

    public IReadOnlyList<GiftReferenceEntry> All { get; }

    public int Count => All.Count;

    public GiftReferenceEntry? FindById(string? id) =>
        !string.IsNullOrWhiteSpace(id) && _byId.TryGetValue(id.Trim(), out var entry) ? entry : null;

    /// <summary>Por nombre, sin mayúsculas, espacios ni signos ("It’s corn" = "its corn").</summary>
    public GiftReferenceEntry? FindByName(string? name) =>
        NameKey(name) is { Length: > 0 } key && _byName.TryGetValue(key, out var entry) ? entry : null;

    /// <summary>Lee tiktok_gifts.json de la carpeta indicada (o de donde lo encuentre <see cref="AppPaths"/>).</summary>
    public static GiftReferenceCatalog Load(string? directory = null)
    {
        var path = directory != null ? System.IO.Path.Combine(directory, FileName) : null;
        if (path == null || !File.Exists(path))
        {
            path = AppPaths.Find(FileName);
        }

        if (path == null || !File.Exists(path))
        {
            return Empty;
        }

        try
        {
            var catalog = new GiftReferenceCatalog(Parse(File.ReadAllText(path, Encoding.UTF8)), path);
            BridgeLog.Info($"Regalos de TikTok: {catalog.Count} en {FileName}");
            return catalog;
        }
        catch (Exception ex)
        {
            BridgeLog.Warn($"{FileName} no se pudo leer ({ex.Message}); se usa solo el catálogo del live.");
            return Empty;
        }
    }

    public static GiftReferenceCatalog FromEntries(IEnumerable<GiftReferenceEntry> entries) => new(entries.ToList(), null);

    public static IReadOnlyList<GiftReferenceEntry> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        });

        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Object && TryGet(root, out var list, "gifts", "regalos"))
        {
            root = list;
        }

        var result = new List<GiftReferenceEntry>();
        if (root.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var el in root.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var id = ReadText(el, "id", "gift_id", "giftId");
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            result.Add(new GiftReferenceEntry(
                id.Trim(),
                (ReadText(el, "nombre_en", "name", "nameEn", "nombre") ?? "").Trim(),
                ReadInt(el, "diamantes", "diamonds", "diamond_count", "diamondCount"),
                (ReadText(el, "icon_url", "iconUrl", "icon") ?? "").Trim(),
                (ReadText(el, "imagen_local", "image", "imageLocal", "imagen") ?? "").Trim()));
        }

        return result;
    }

    private static string NameKey(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? ""
            : new string(name.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static bool TryGet(JsonElement el, out JsonElement value, params string[] names)
    {
        foreach (var name in names)
        {
            if (el.TryGetProperty(name, out value) && value.ValueKind != JsonValueKind.Null)
            {
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? ReadText(JsonElement el, params string[] names)
    {
        if (!TryGet(el, out var value, names))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }

    private static int? ReadInt(JsonElement el, params string[] names)
    {
        if (!TryGet(el, out var value, names))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed) ? parsed : null;
    }
}
