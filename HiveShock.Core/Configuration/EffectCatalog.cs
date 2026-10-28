using System.Globalization;
using HiveShock.Input;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace HiveShock.Configuration;

public sealed class EffectCatalog
{
    private readonly Dictionary<string, Dictionary<string, JsonElement>> _effects;
    private readonly Dictionary<string, string> _labels;
    private readonly HashSet<string> _enemyEffectIds;
    private readonly HashSet<string> _singletonEffectIds;
    private volatile KeyMacroCatalog _macros;

    public EffectCatalog(
        Dictionary<string, Dictionary<string, JsonElement>> effects,
        IReadOnlyDictionary<string, string>? labels = null,
        IEnumerable<string>? enemyEffects = null,
        IEnumerable<string>? singletonEffects = null,
        KeyMacroCatalog? macros = null)
    {
        _effects = effects;
        _macros = macros ?? KeyMacroCatalog.Empty;
        _labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (labels != null)
        {
            foreach (var (k, v) in labels)
            {
                if (!string.IsNullOrWhiteSpace(k) && !string.IsNullOrWhiteSpace(v))
                {
                    _labels[k] = v;
                }
            }
        }

        _enemyEffectIds = new HashSet<string>(
            enemyEffects?.Where(s => !string.IsNullOrWhiteSpace(s)) ?? [],
            StringComparer.OrdinalIgnoreCase);
        _singletonEffectIds = new HashSet<string>(
            singletonEffects?.Where(s => !string.IsNullOrWhiteSpace(s)) ??
            ["delete_save", "wipe_save", "nuke_save", "new_run"],
            StringComparer.OrdinalIgnoreCase);
    }

    public static EffectCatalog Load(string path, GameProfileInfo? profile = null, KeyMacroCatalog? macros = null)
    {
        var json = File.ReadAllText(path, Encoding.UTF8);
        var parsed = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, JsonElement>>>(
            json,
            JsonDefaults.Options) ?? new();
        return new EffectCatalog(
            parsed,
            profile?.Labels,
            profile?.EnemyEffects,
            profile?.SingletonEffects,
            macros);
    }

    /// <summary>
    /// Cambia las macros en caliente. El catálogo es el mismo objeto que usa el dispatcher del puente en marcha,
    /// así que las macros editadas valen al instante sin reconectar.
    /// </summary>
    public void SetMacros(KeyMacroCatalog macros) => _macros = macros;

    /// <summary>Efectos del sistema que este equipo puede ejecutar (p. ej. reiniciar). Vacío por defecto.</summary>
    public void SetSystemEffects(IReadOnlyList<string> ids) => _systemIds = ids ?? [];

    private volatile IReadOnlyList<string> _systemIds = [];

    private bool IsSystem(string effectId) =>
        !string.IsNullOrWhiteSpace(effectId) &&
        !_effects.ContainsKey(effectId) &&
        _systemIds.Contains(effectId, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<KeyMacro> Macros => _macros.All;

    /// <summary>Efectos del juego más macros de teclas (un efecto de effects.json con el mismo id gana).</summary>
    public IEnumerable<string> Ids => _effects.Keys
        .Concat(_macros.All.Select(m => m.Id).Where(id => !_effects.ContainsKey(id)))
        .Concat(_systemIds.Where(id => !_effects.ContainsKey(id)))
        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);

    public int Count => Ids.Count();

    public bool Contains(string effectId) =>
        !string.IsNullOrWhiteSpace(effectId) &&
        (_effects.ContainsKey(effectId) || _macros.Contains(effectId) || IsSystem(effectId));

    /// <summary>Macro de teclas con ese id; false si no existe o si effects.json define un efecto con ese id.</summary>
    public bool TryGetMacro(string effectId, out KeyMacro macro)
    {
        macro = null!;
        return !string.IsNullOrWhiteSpace(effectId) &&
               !_effects.ContainsKey(effectId) &&
               _macros.TryGet(effectId, out macro);
    }

    public bool IsSingletonEffect(string effectId) =>
        _singletonEffectIds.Contains(effectId);

    public string? GetAction(string effectId)
    {
        if (!_effects.TryGetValue(effectId, out var template))
        {
            if (IsSystem(effectId))
            {
                return "system";
            }

            return TryGetMacro(effectId, out _) ? "keys" : null;
        }

        if (template.TryGetValue("action", out var action) && action.ValueKind == JsonValueKind.String)
        {
            return action.GetString();
        }

        return null;
    }

    public string Describe(string effectId)
    {
        if (IsSystem(effectId))
        {
            return "Sistema: " + (string.Equals(effectId, "reiniciar_pc", StringComparison.OrdinalIgnoreCase)
                ? "Reiniciar el equipo (con cuenta atrás)"
                : effectId);
        }

        if (TryGetMacro(effectId, out var macro))
        {
            return string.IsNullOrWhiteSpace(macro.Label) ? $"Teclas: {macro.Id}" : macro.Label;
        }

        var action = GetAction(effectId) ?? effectId;
        if (_labels.TryGetValue(action, out var byAction))
        {
            return byAction;
        }

        if (_labels.TryGetValue(effectId, out var byId))
        {
            return byId;
        }

        return action.Replace('_', ' ');
    }

    public IReadOnlyList<(string Id, string Label)> ListEntries(string? filter = null)
    {
        IEnumerable<string> ids = Ids;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            var f = filter.Trim();
            ids = ids.Where(id =>
                id.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                Describe(id).Contains(f, StringComparison.OrdinalIgnoreCase));
        }

        return ids
            .Select(id => (Id: id, Label: Describe(id), IsEnemy: IsEnemyEffect(id)))
            .OrderByDescending(x => x.IsEnemy)
            .ThenBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
            .Select(x => (x.Id, x.Label))
            .ToList();
    }

    public bool IsEnemyEffect(string effectId) =>
        _enemyEffectIds.Contains(effectId);

    public IReadOnlyList<EffectParamDef> GetParamSchema(string effectId)
    {
        if (string.IsNullOrWhiteSpace(effectId) || !_effects.TryGetValue(effectId, out var template))
        {
            return [];
        }

        return ParseParamSchema(template);
    }

    public Dictionary<string, object?>? Resolve(string effectId, string? user) =>
        Resolve(effectId, user, null);

    public Dictionary<string, object?>? Resolve(
        string effectId,
        string? user,
        IReadOnlyDictionary<string, double>? overrides)
    {
        if (string.IsNullOrWhiteSpace(effectId) || effectId.StartsWith('_'))
        {
            return null;
        }

        if (!_effects.TryGetValue(effectId, out var template))
        {
            if (_macros.Contains(effectId) || IsSystem(effectId))
            {
                // Macro de teclas o efecto del sistema: no tienen comando TCP; los ejecuta su propio servicio.
                return null;
            }

            Console.Error.WriteLine($"[{ProductInfo.Name}] Efecto desconocido: {effectId}");
            return null;
        }

        var cmd = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in template)
        {
            if (key.StartsWith('_'))
            {
                continue;
            }

            cmd[key] = JsonElementToObject(value);
        }

        if (overrides is { Count: > 0 })
        {
            var schema = ParseParamSchema(template);
            foreach (var (key, wireValue) in overrides)
            {
                if (string.IsNullOrWhiteSpace(key) || key.StartsWith('_'))
                {
                    continue;
                }

                var def = schema.FirstOrDefault(p =>
                    string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
                if (def == null)
                {
                    continue;
                }

                cmd[def.Key] = ToJsonNumber(def.ClampWire(wireValue));
            }
        }

        if (!string.IsNullOrWhiteSpace(user))
        {
            cmd["user"] = user;
        }

        return cmd;
    }

    private static IReadOnlyList<EffectParamDef> ParseParamSchema(Dictionary<string, JsonElement> template)
    {
        if (!template.TryGetValue("_params", out var paramsEl) || paramsEl.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var list = new List<EffectParamDef>();
        foreach (var prop in paramsEl.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var o = prop.Value;
            list.Add(new EffectParamDef
            {
                Key = prop.Name,
                Label = ReadParamString(o, "label") ?? prop.Name,
                Unit = ReadParamString(o, "unit") ?? "raw",
                Scale = ReadParamDouble(o, "scale") ?? 1,
                Min = ReadParamDouble(o, "min") ?? 0,
                Max = ReadParamDouble(o, "max") ?? 100,
                Step = ReadParamDouble(o, "step") ?? 1,
            });
        }

        return list;
    }

    private static string? ReadParamString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    private static double? ReadParamDouble(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return el.TryGetDouble(out var d) ? d : null;
    }

    private static object ToJsonNumber(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0L;
        }

        var rounded = Math.Round(value, 6);
        if (Math.Abs(rounded - Math.Round(rounded)) < 0.0000001)
        {
            return (long)Math.Round(rounded);
        }

        return rounded;
    }

    private static object? JsonElementToObject(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number when el.TryGetInt64(out var l) => l,
        JsonValueKind.Number => el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => el.GetRawText(),
    };
}
