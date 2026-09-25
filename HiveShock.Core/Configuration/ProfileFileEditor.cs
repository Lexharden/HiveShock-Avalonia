using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HiveShock.Configuration;

public sealed class EditableLabel
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

/// <summary>Copia editable de profile.json (metadatos del perfil; no las rutas resueltas en disco).</summary>
public sealed class EditableProfileInfo
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public string GameHost { get; set; } = "127.0.0.1";
    public int GamePort { get; set; } = 43000;
    public int CrowdControlPort { get; set; } = 43001;
    public int EventPort { get; set; } = 43002;
    public string Protocol { get; set; } = "json-line";
    public bool SupportsDeathEvents { get; set; } = true;
    public bool SupportsDeleteSave { get; set; }
    public bool SupportsRescue { get; set; }
    public string HelpNotes { get; set; } = "";
    public List<EditableLabel> Labels { get; } = [];
    public List<string> EnemyEffects { get; } = [];
    public List<string> SingletonEffects { get; } = [];
}

/// <summary>
/// Edita profile.json de un perfil: metadatos, puertos, notas de ayuda, etiquetas
/// de efectos y las listas de "enemigo" / "singleton". Separado de <see cref="GiftFileEditor"/>
/// porque vive en un archivo propio y se importa/exporta por su cuenta.
/// </summary>
public sealed class ProfileInfoEditor
{
    private readonly string _path;

    public ProfileInfoEditor(string path) => _path = path;

    public EditableProfileInfo Info { get; private set; } = new();

    public void Load() => Info = ReadFrom(_path);

    public void Save() => WriteTo(_path, Info);

    /// <summary>Lee un profile.json de cualquier ruta (botón Importar) sin tocar el actual hasta guardar.</summary>
    public static EditableProfileInfo ImportFrom(string path) =>
        ParseJson(File.ReadAllText(path, Encoding.UTF8));

    /// <summary>Igual que <see cref="ImportFrom(string)"/> pero desde un stream (Android: SAF entrega content:// URIs, no rutas).</summary>
    public static async Task<EditableProfileInfo> ImportFromAsync(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return ParseJson(await reader.ReadToEndAsync().ConfigureAwait(false));
    }

    /// <summary>
    /// Aplica un profile.json importado como estado en memoria (la UI lo revisa antes de
    /// pulsar Guardar). El id se conserva: profile.json vive en la carpeta del perfil activo,
    /// no tiene sentido que un import cambie a qué perfil pertenece.
    /// </summary>
    public void ReplaceInMemory(EditableProfileInfo imported)
    {
        imported.Id = Info.Id;
        Info = imported;
    }

    /// <summary>Vuelca el estado actual (editado o no) a otra ruta (botón Exportar).</summary>
    public void ExportTo(string destinationPath) => WriteTo(destinationPath, Info);

    /// <summary>Igual que <see cref="ExportTo(string)"/> pero a un stream (Android: SAF entrega content:// URIs).</summary>
    public async Task ExportToAsync(Stream stream)
    {
        var bytes = Encoding.UTF8.GetBytes(ToJson(Info));
        await stream.WriteAsync(bytes).ConfigureAwait(false);
    }

    private static EditableProfileInfo ReadFrom(string path) => ParseJson(File.ReadAllText(path, Encoding.UTF8));

    private static EditableProfileInfo ParseJson(string json)
    {
        var info = JsonSerializer.Deserialize<GameProfileInfo>(json, JsonDefaults.Options)
                   ?? throw new InvalidDataException("profile.json inválido.");

        var result = new EditableProfileInfo
        {
            Id = info.Id,
            DisplayName = info.DisplayName,
            Description = info.Description,
            GameHost = info.GameHost,
            GamePort = info.GamePort,
            CrowdControlPort = info.CrowdControlPort,
            EventPort = info.EventPort,
            Protocol = info.Protocol,
            SupportsDeathEvents = info.SupportsDeathEvents,
            SupportsDeleteSave = info.SupportsDeleteSave,
            SupportsRescue = info.SupportsRescue,
            HelpNotes = info.HelpNotes,
        };

        foreach (var (key, value) in info.Labels)
        {
            result.Labels.Add(new EditableLabel { Key = key, Value = value });
        }

        result.EnemyEffects.AddRange(info.EnemyEffects);
        result.SingletonEffects.AddRange(info.SingletonEffects);
        return result;
    }

    private static void WriteTo(string path, EditableProfileInfo info) =>
        File.WriteAllText(path, ToJson(info), new UTF8Encoding(false));

    private static string ToJson(EditableProfileInfo info)
    {
        var model = new GameProfileInfo
        {
            Id = info.Id.Trim(),
            DisplayName = info.DisplayName.Trim(),
            Description = (info.Description ?? "").Trim(),
            GameHost = string.IsNullOrWhiteSpace(info.GameHost) ? "127.0.0.1" : info.GameHost.Trim(),
            GamePort = info.GamePort > 0 ? info.GamePort : 43000,
            CrowdControlPort = info.CrowdControlPort > 0 ? info.CrowdControlPort : 43001,
            EventPort = info.EventPort > 0 ? info.EventPort : 43002,
            Protocol = string.IsNullOrWhiteSpace(info.Protocol) ? "json-line" : info.Protocol.Trim(),
            SupportsDeathEvents = info.SupportsDeathEvents,
            SupportsDeleteSave = info.SupportsDeleteSave,
            SupportsRescue = info.SupportsRescue,
            HelpNotes = info.HelpNotes ?? "",
        };

        foreach (var label in info.Labels)
        {
            if (string.IsNullOrWhiteSpace(label.Key))
            {
                continue;
            }

            model.Labels[label.Key.Trim()] = label.Value ?? "";
        }

        model.EnemyEffects.AddRange(
            info.EnemyEffects.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim()));
        model.SingletonEffects.AddRange(
            info.SingletonEffects.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim()));

        return JsonSerializer.Serialize(model, IndentedOptions) + Environment.NewLine;
    }

    private static readonly JsonSerializerOptions IndentedOptions = new(JsonDefaults.Options)
    {
        WriteIndented = true,
    };
}

/// <summary>Tipo de valor de una propiedad de efecto, para que la UI ofrezca el control correcto.</summary>
public enum EditableValueKind
{
    Text,
    Number,
    Bool,
}

public sealed class EditableEffectProperty
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public EditableValueKind Kind { get; set; } = EditableValueKind.Text;
}

public sealed class EditableEffect
{
    public string Id { get; set; } = "";
    public List<EditableEffectProperty> Properties { get; } = [];
}

/// <summary>
/// Edita effects.json de un perfil: el catálogo de comandos que se mandan al juego.
/// Cada efecto es una bolsa libre de propiedades (el juego define su propio esquema),
/// así que se editan como pares clave/valor en vez de forzar campos fijos.
/// </summary>
public sealed class EffectCatalogEditor
{
    private readonly string _path;

    public EffectCatalogEditor(string path) => _path = path;

    public List<EditableEffect> Effects { get; private set; } = [];

    public void Load() => Effects = ReadFrom(_path);

    public void Save() => WriteTo(_path, Effects);

    public static List<EditableEffect> ImportFrom(string path) => ParseJson(File.ReadAllText(path, Encoding.UTF8));

    /// <summary>Igual que <see cref="ImportFrom(string)"/> pero desde un stream (Android: SAF entrega content:// URIs).</summary>
    public static async Task<List<EditableEffect>> ImportFromAsync(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return ParseJson(await reader.ReadToEndAsync().ConfigureAwait(false));
    }

    /// <summary>Aplica un effects.json importado como estado en memoria, pendiente de Guardar.</summary>
    public void ReplaceInMemory(List<EditableEffect> imported) => Effects = imported;

    public void ExportTo(string destinationPath) => WriteTo(destinationPath, Effects);

    /// <summary>Igual que <see cref="ExportTo(string)"/> pero a un stream (Android: SAF entrega content:// URIs).</summary>
    public async Task ExportToAsync(Stream stream)
    {
        var bytes = Encoding.UTF8.GetBytes(ToJson(Effects));
        await stream.WriteAsync(bytes).ConfigureAwait(false);
    }

    private static List<EditableEffect> ReadFrom(string path) => ParseJson(File.ReadAllText(path, Encoding.UTF8));

    private static List<EditableEffect> ParseJson(string json)
    {
        var parsed = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, JsonElement>>>(
            json, JsonDefaults.Options) ?? new();

        var list = new List<EditableEffect>();
        foreach (var (id, props) in parsed)
        {
            var effect = new EditableEffect { Id = id };
            foreach (var (key, value) in props)
            {
                effect.Properties.Add(new EditableEffectProperty
                {
                    Key = key,
                    Value = ValueToText(value),
                    Kind = ValueToKind(value),
                });
            }

            list.Add(effect);
        }

        return list.OrderBy(e => e.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string ValueToText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? "",
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => "",
        _ => value.GetRawText(),
    };

    private static EditableValueKind ValueToKind(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => EditableValueKind.Number,
        JsonValueKind.True or JsonValueKind.False => EditableValueKind.Bool,
        _ => EditableValueKind.Text,
    };

    private static void WriteTo(string path, List<EditableEffect> effects) =>
        File.WriteAllText(path, ToJson(effects), new UTF8Encoding(false));

    private static string ToJson(List<EditableEffect> effects)
    {
        var root = new JsonObject();
        foreach (var effect in effects)
        {
            if (string.IsNullOrWhiteSpace(effect.Id))
            {
                continue;
            }

            var obj = new JsonObject();
            foreach (var prop in effect.Properties)
            {
                if (string.IsNullOrWhiteSpace(prop.Key))
                {
                    continue;
                }

                obj[prop.Key.Trim()] = ValueToNode(prop);
            }

            root[effect.Id.Trim()] = obj;
        }

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
    }

    private static JsonNode? ValueToNode(EditableEffectProperty prop)
    {
        switch (prop.Kind)
        {
            case EditableValueKind.Number:
                if (long.TryParse(prop.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
                {
                    return JsonValue.Create(l);
                }

                return double.TryParse(prop.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                    ? JsonValue.Create(d)
                    : JsonValue.Create(0);
            case EditableValueKind.Bool:
                return JsonValue.Create(string.Equals(prop.Value, "true", StringComparison.OrdinalIgnoreCase));
            default:
                return JsonValue.Create(prop.Value ?? "");
        }
    }
}
