using System.Text.Json;

namespace HiveShock.Zeldathon;

/// <summary>
/// Items and objectives the race server accepts. The server owns it (organizers edit it in the admin
/// panel); HiveShock downloads it when it connects and keeps this built-in copy of the factory catalog
/// for when the server cannot be reached. Ids the server does not know are rejected with <c>invalid</c>,
/// so <see cref="ZeldathonSession"/> checks them here first.
/// </summary>
public sealed class ZeldathonCatalog
{
    private readonly HashSet<string> _items;
    private readonly HashSet<string> _objectives;

    public ZeldathonCatalog(
        string version,
        IEnumerable<string> items,
        IEnumerable<string> objectivesInOrder,
        IEnumerable<string>? required = null)
    {
        Version = version;
        _items = new HashSet<string>(items, StringComparer.Ordinal);
        Objectives = objectivesInOrder.ToList();
        _objectives = new HashSet<string>(Objectives, StringComparer.Ordinal);
        Required = (required ?? Objectives).Where(_objectives.Contains).ToList();
    }

    /// <summary>Content hash from the server ("factory" for the built-in copy).</summary>
    public string Version { get; }

    public IReadOnlyCollection<string> Items => _items;

    /// <summary>Objective ids in race order (used for progress and "current objective").</summary>
    public IReadOnlyList<string> Objectives { get; }

    /// <summary>Objectives that count toward finishing when the event's own rules are not known yet.</summary>
    public IReadOnlyList<string> Required { get; }

    public bool IsItem(string id) => _items.Contains(id);

    public bool IsObjective(string id) => _objectives.Contains(id);

    /// <summary>Parses the body of <c>GET /api/catalog</c> (enabled entries, already in display order).</summary>
    public static ZeldathonCatalog Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var items = new List<string>();
        if (root.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in itemsEl.EnumerateArray())
            {
                if (el.TryGetProperty("id", out var id) && id.GetString() is { Length: > 0 } text &&
                    (!el.TryGetProperty("enabled", out var on) || on.ValueKind != JsonValueKind.False))
                {
                    items.Add(text);
                }
            }
        }

        var objectives = new List<(int Order, string Id, bool Required)>();
        if (root.TryGetProperty("objectives", out var objEl) && objEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in objEl.EnumerateArray())
            {
                if (!el.TryGetProperty("id", out var id) || id.GetString() is not { Length: > 0 } text ||
                    (el.TryGetProperty("enabled", out var on) && on.ValueKind == JsonValueKind.False))
                {
                    continue;
                }

                var order = el.TryGetProperty("sortOrder", out var so) && so.TryGetInt32(out var n) ? n : 0;
                var required = !el.TryGetProperty("required", out var rq) || rq.ValueKind != JsonValueKind.False;
                objectives.Add((order, text, required));
            }
        }

        if (items.Count == 0 && objectives.Count == 0)
        {
            throw new JsonException("El catálogo del servidor está vacío.");
        }

        var ordered = objectives.OrderBy(o => o.Order).ThenBy(o => o.Id, StringComparer.Ordinal).ToList();
        var version = root.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "";
        return new ZeldathonCatalog(version, items, ordered.Select(o => o.Id), ordered.Where(o => o.Required).Select(o => o.Id));
    }

    /// <summary>The factory catalog (Ocarina of Time), same ids the server seeds.</summary>
    public static ZeldathonCatalog Default { get; } = new(
        "factory",
        [
            // Link niño
            "kokiri-sword", "deku-shield", "fairy-slingshot", "boomerang", "mask-of-truth", "zeldas-letter",
            "kokiri-emerald", "goron-ruby", "zora-sapphire",
            // Link adulto
            "master-sword", "biggoron-sword", "megaton-hammer", "mirror-shield", "goron-tunic", "zora-tunic",
            "iron-boots", "hover-boots", "bow", "hookshot", "longshot", "fire-arrows", "ice-arrows", "light-arrows",
            "silver-gauntlets", "golden-gauntlets", "gerudo-card", "forest-medallion", "fire-medallion",
            "water-medallion", "shadow-medallion", "spirit-medallion", "light-medallion",
            // Ambos
            "bombs", "bombchus", "hylian-shield", "ocarina-of-time", "lens-of-truth", "bottle", "dins-fire",
            "farores-wind", "nayrus-love", "bomb-bag", "wallet", "goron-bracelet", "silver-scale", "golden-scale",
            "magic-meter", "double-defense", "stone-of-agony",
            // Canciones
            "zeldas-lullaby", "eponas-song", "sarias-song", "suns-song", "song-of-time", "song-of-storms",
            "minuet-of-forest", "bolero-of-fire", "serenade-of-water", "requiem-of-spirit", "nocturne-of-shadow",
            "prelude-of-light",
        ],
        [
            "kokiri-forest", "deku-tree", "dodongos-cavern", "jabu-jabu", "forest-temple", "fire-temple",
            "water-temple", "shadow-temple", "spirit-temple", "ganons-castle",
        ]);
}
