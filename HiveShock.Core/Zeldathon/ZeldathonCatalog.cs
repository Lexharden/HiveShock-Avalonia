namespace HiveShock.Zeldathon;

/// <summary>Ids que acepta el servidor (backend/src/catalog.rs y src/config/event.ts de la web).</summary>
public static class ZeldathonCatalog
{
    public static readonly IReadOnlyList<string> Objectives =
    [
        "kokiri-forest", "deku-tree", "dodongos-cavern", "jabu-jabu", "forest-temple",
        "fire-temple", "water-temple", "shadow-temple", "spirit-temple", "ganons-castle",
    ];

    public static readonly IReadOnlyList<string> Items =
    [
        "master-sword", "hookshot", "longshot", "bow", "bombs", "boomerang",
        "megaton-hammer", "iron-boots", "mirror-shield",
    ];

    public static bool IsObjective(string id) => Objectives.Contains(id, StringComparer.Ordinal);

    public static bool IsItem(string id) => Items.Contains(id, StringComparer.Ordinal);
}
