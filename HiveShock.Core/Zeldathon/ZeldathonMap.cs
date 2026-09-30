using System.Text.Json;
using HiveShock.Logging;

namespace HiveShock.Zeldathon;

/// <summary>
/// Traducción de lo que manda el juego (números) a los ids del catálogo de Zeldathon. Vive en un
/// archivo por perfil (<c>zeldathon.json</c>) para poder ajustarla sin recompilar el juego.
/// </summary>
public sealed class ZeldathonMap
{
    public const string FileName = "zeldathon.json";
    public const int MaxTextLength = ZeldathonProtocol.MaxTextLength;

    public Dictionary<int, string> Areas { get; } = new();
    public Dictionary<int, string> Items { get; } = new();
    public Dictionary<int, string> Bosses { get; } = new();

    /// <summary>Bit de <c>questItems</c> (medallas y piedras) → objetivo.</summary>
    public Dictionary<int, string> QuestObjectives { get; } = new();

    /// <summary>Ítem del juego que completa un objetivo (p. ej. la espada Kokiri).</summary>
    public Dictionary<int, string> ItemObjectives { get; } = new();

    /// <summary>Jefe (id de jefe) que completa un objetivo.</summary>
    public Dictionary<string, string> BossObjectives { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Jefe cuya derrota termina el juego.</summary>
    public string FinishBoss { get; private set; } = "";

    public bool IsEmpty => Areas.Count == 0 && Items.Count == 0 && Bosses.Count == 0 && QuestObjectives.Count == 0;

    public static ZeldathonMap Load(string path)
    {
        var map = new ZeldathonMap();
        if (!File.Exists(path))
        {
            return map;
        }

        try
        {
            map.Fill(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            BridgeLog.Warn($"zeldathon.json: {ex.Message}");
            return new ZeldathonMap();
        }

        return map;
    }

    /// <summary>
    /// El <c>zeldathon.json</c> de la carpeta del perfil manda; si no existe, se usa la copia de fábrica
    /// incluida en el programa para ese perfil (si la hay).
    /// </summary>
    public static ZeldathonMap LoadForProfile(string profileDirectory, string profileId)
    {
        var path = Path.Combine(profileDirectory, FileName);
        if (File.Exists(path))
        {
            return Load(path);
        }

        using var stream = typeof(ZeldathonMap).Assembly.GetManifestResourceStream($"zeldathon.{profileId}.json");
        if (stream == null)
        {
            return new ZeldathonMap();
        }

        try
        {
            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd());
        }
        catch (JsonException ex)
        {
            BridgeLog.Warn($"zeldathon.json de fábrica: {ex.Message}");
            return new ZeldathonMap();
        }
    }

    public static ZeldathonMap Parse(string json)
    {
        var map = new ZeldathonMap();
        map.Fill(json);
        return map;
    }

    private void Fill(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("zeldathon.json debe ser un objeto.");
        }

        ReadNumbered(root, "areas", Areas, text => text.Length is > 0 and <= MaxTextLength, "área");
        ReadNumbered(root, "items", Items, ZeldathonCatalog.IsItem, "ítem");
        ReadNumbered(root, "bosses", Bosses, text => text.Length is > 0 and <= MaxTextLength, "jefe");

        if (root.TryGetProperty("objectives", out var obj) && obj.ValueKind == JsonValueKind.Object)
        {
            ReadNumbered(obj, "quest", QuestObjectives, ZeldathonCatalog.IsObjective, "objetivo");
            ReadNumbered(obj, "items", ItemObjectives, ZeldathonCatalog.IsObjective, "objetivo");
            if (obj.TryGetProperty("bosses", out var bosses) && bosses.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in bosses.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String && ZeldathonCatalog.IsObjective(prop.Value.GetString() ?? ""))
                    {
                        BossObjectives[prop.Name] = prop.Value.GetString()!;
                    }
                    else
                    {
                        BridgeLog.Warn($"zeldathon.json: objetivo desconocido para el jefe «{prop.Name}»");
                    }
                }
            }
        }

        if (root.TryGetProperty("finishBoss", out var finish) && finish.ValueKind == JsonValueKind.String)
        {
            FinishBoss = finish.GetString() ?? "";
        }
    }

    /// <summary>Lee un objeto {"número": "id"}; lo que no cumple <paramref name="valid"/> se descarta con aviso.</summary>
    private static void ReadNumbered(JsonElement parent, string name, Dictionary<int, string> into, Func<string, bool> valid, string what)
    {
        if (!parent.TryGetProperty(name, out var section) || section.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var prop in section.EnumerateObject())
        {
            var value = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() ?? "" : "";
            if (!int.TryParse(prop.Name, out var key) || !valid(value))
            {
                BridgeLog.Warn($"zeldathon.json: {what} «{prop.Name}» → «{value}» no es válido y se ignora");
                continue;
            }

            into[key] = value;
        }
    }
}
