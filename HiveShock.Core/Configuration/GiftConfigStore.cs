using System.Globalization;
using HiveShock.Input;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace HiveShock.Configuration;

public sealed class GiftConfigStore
{
    private static readonly HashSet<string> MetaKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "_help", "_comment", "gifts", "likes", "follow", "share", "chat", "twitch", "tiktok",
        "unmappedGifts", "giftsByDiamonds",
    };

    private readonly object _gate = new();
    private readonly EffectCatalog _effects;
    private GiftRuntimeConfig _current = new();

    public GiftConfigStore(EffectCatalog effects) => _effects = effects;

    public GiftRuntimeConfig Snapshot
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public void Reload(string giftsPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(giftsPath, Encoding.UTF8));
        var root = doc.RootElement;
        var parsed = Parse(root);

        lock (_gate)
        {
            _current = parsed;
        }
    }

    public string? ResolveEffect(string? name, string? id, long diamonds) =>
        ResolveGift(name, id, diamonds)?.EffectId;

    public GiftMatch? ResolveGift(string? name, string? id, long diamonds)
    {
        var cfg = Snapshot;
        var nName = GiftKeyNormalizer.Normalize(name);
        var nId = GiftKeyNormalizer.Normalize(id);

        foreach (var rule in cfg.Rules)
        {
            if (!string.IsNullOrEmpty(nName) && rule.Normalized == nName)
            {
                return ToMatch(rule);
            }

            if (!string.IsNullOrEmpty(nId) && rule.Normalized == nId)
            {
                return ToMatch(rule);
            }
        }

        if (!string.Equals(cfg.UnmappedGifts, "diamonds", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        foreach (var row in cfg.GiftsByDiamonds)
        {
            var max = row.Max ?? int.MaxValue;
            if (diamonds >= row.Min && diamonds <= max)
            {
                return new GiftMatch
                {
                    EffectId = row.Effect,
                    MinCount = 1,
                    Each = false,
                    Plan = EffectPlan.Single(row.Effect),
                };
            }
        }

        return null;
    }

    private static GiftMatch ToMatch(GiftRule rule) =>
        new()
        {
            EffectId = rule.EffectId,
            MinCount = Math.Max(1, rule.MinCount),
            Each = rule.Each,
            Params = rule.Params,
            Plan = rule.Plan ?? EffectPlan.Single(rule.EffectId, rule.Params),
        };

    public void PrintRules()
    {
        var groups = Snapshot.Groups;
        if (groups.Count == 0)
        {
            Console.WriteLine("  (sin mapeos)");
            return;
        }

        foreach (var group in groups)
        {
            Console.WriteLine($"  {string.Join(" / ", group.Names)}  →  {group.EffectId}");
        }
    }

    private GiftRuntimeConfig Parse(JsonElement root)
    {
        var rules = new List<GiftRule>();
        var groups = new List<GiftGroup>();

        if (root.TryGetProperty("gifts", out var giftsEl) && giftsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in giftsEl.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var effectId = ParseEffect(row);
                var what = GetString(row, "what") ?? GetString(row, "note") ?? "";
                var minCount = ParsePositiveInt(row, "minCount") ?? ParsePositiveInt(row, "min") ?? 1;
                var each = ParseBool(row, "each") || ParseBool(row, "perGift");
                var paramOverrides = ParseParamOverrides(row);
                var plan = ParsePlan(row, effectId, paramOverrides);
                if (!plan.IsEmpty)
                {
                    effectId = plan.EffectId;
                }

                var names = CollectNames(row);
                var groupNames = new List<string>();
                foreach (var name in names)
                {
                    if (TryAddRule(rules, name, effectId, what, minCount, each, paramOverrides, plan))
                    {
                        groupNames.Add(name);
                    }
                }

                if (groupNames.Count > 0)
                {
                    groups.Add(new GiftGroup(groupNames, effectId, what));
                }
            }
        }
        else
        {
            var source = root.TryGetProperty("gifts", out var giftsObj) && giftsObj.ValueKind == JsonValueKind.Object
                ? giftsObj
                : root;

            foreach (var prop in source.EnumerateObject())
            {
                if (prop.Name.StartsWith('_') || MetaKeys.Contains(prop.Name))
                {
                    continue;
                }

                var effectId = ParseEffect(prop.Value);
                var what = prop.Value.ValueKind == JsonValueKind.Object
                    ? GetString(prop.Value, "what") ?? ""
                    : "";
                var minCount = prop.Value.ValueKind == JsonValueKind.Object
                    ? ParsePositiveInt(prop.Value, "minCount") ?? ParsePositiveInt(prop.Value, "min") ?? 1
                    : 1;
                var each = prop.Value.ValueKind == JsonValueKind.Object &&
                           (ParseBool(prop.Value, "each") || ParseBool(prop.Value, "perGift"));
                var paramOverrides = prop.Value.ValueKind == JsonValueKind.Object
                    ? ParseParamOverrides(prop.Value)
                    : null;
                var plan = ParsePlan(prop.Value, effectId, paramOverrides);
                if (!plan.IsEmpty)
                {
                    effectId = plan.EffectId;
                }

                if (TryAddRule(rules, prop.Name, effectId, what, minCount, each, paramOverrides, plan))
                {
                    groups.Add(new GiftGroup([prop.Name], effectId, what));
                }
            }
        }

        var chat = ParseChat(root);
        var follow = ParseSimple(root, "follow");
        var twitchChat = chat;
        var twitchFollow = follow;
        var twitchBits = (IReadOnlyList<BitsBracket>)[];
        if (root.TryGetProperty("twitch", out var twitch) && twitch.ValueKind == JsonValueKind.Object)
        {
            twitchChat = ParseChat(twitch);
            twitchFollow = ParseSimple(twitch, "follow");
            twitchBits = ParseBitsBrackets(twitch);
        }

        return new GiftRuntimeConfig
        {
            Rules = rules,
            Groups = groups,
            UnmappedGifts = GetString(root, "unmappedGifts") ?? "ignore",
            GiftsByDiamonds = ParseDiamondBrackets(root),
            Likes = ParseLikes(root),
            Follow = follow,
            Share = ParseSimple(root, "share"),
            Chat = chat,
            TwitchFollow = twitchFollow,
            TwitchChat = twitchChat,
            TwitchBits = twitchBits,
            Goals = ParseGoals(root),
        };
    }

    private bool TryAddRule(
        List<GiftRule> rules,
        string key,
        string effectId,
        string what,
        int minCount = 1,
        bool each = false,
        IReadOnlyDictionary<string, double>? paramOverrides = null,
        EffectPlan? plan = null)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        // Los pasos con un efecto que no existe se omiten con aviso; si no queda ninguno, la regla se descarta.
        var wanted = plan is { IsEmpty: false } ? plan : EffectPlan.Single(effectId, paramOverrides);
        var valid = new List<EffectStep>(wanted.Steps.Count);
        foreach (var step in wanted.Steps)
        {
            if (_effects.Contains(step.EffectId))
            {
                valid.Add(step);
            }
            else
            {
                Console.Error.WriteLine($"[gifts.json] \"{key}\" apunta a un efecto que no existe: {step.EffectId}");
            }
        }

        var final = EffectPlan.FromSteps(valid);
        if (final.IsEmpty)
        {
            return false;
        }

        var first = final.Steps[0];
        rules.Add(new GiftRule(
            key,
            GiftKeyNormalizer.Normalize(key),
            first.EffectId,
            what.Trim(),
            Math.Max(1, minCount),
            each,
            first.Params,
            final));
        return true;
    }

    /// <summary>
    /// Efectos de un regalo: la lista <c>effects</c> si existe (cada paso es un texto o un objeto con
    /// <c>effect</c>, <c>params</c> y <c>delaySec</c>); si no, el <c>effect</c> + <c>params</c> de siempre.
    /// </summary>
    private static EffectPlan ParsePlan(
        JsonElement row,
        string effectId,
        IReadOnlyDictionary<string, double>? paramOverrides)
    {
        if (row.ValueKind == JsonValueKind.Object &&
            row.TryGetProperty("effects", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            var steps = new List<EffectStep>();
            foreach (var item in list.EnumerateArray())
            {
                var id = ParseEffect(item);
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                var delayMs = 0;
                if (item.ValueKind == JsonValueKind.Object &&
                    item.TryGetProperty("delaySec", out var delayEl) &&
                    delayEl.ValueKind == JsonValueKind.Number && delayEl.TryGetDouble(out var delaySec))
                {
                    delayMs = EffectPlan.DelayToMs(delaySec);
                }

                steps.Add(new EffectStep(
                    id,
                    item.ValueKind == JsonValueKind.Object ? ParseParamOverrides(item) : null,
                    delayMs));
            }

            // Sin espera antes del primer efecto: el regalo se lanza al llegar.
            if (steps.Count > 0)
            {
                steps[0] = steps[0] with { DelayMs = 0 };
                return EffectPlan.FromSteps(steps);
            }
        }

        return EffectPlan.Single(effectId, paramOverrides);
    }

    private static IReadOnlyDictionary<string, double>? ParseParamOverrides(JsonElement row)
    {
        if (!row.TryGetProperty("params", out var paramsEl) || paramsEl.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var dict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in paramsEl.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Number || !prop.Value.TryGetDouble(out var value))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(prop.Name) || prop.Name.StartsWith('_'))
            {
                continue;
            }

            dict[prop.Name] = value;
        }

        return dict.Count > 0 ? dict : null;
    }

    private static int? ParsePositiveInt(JsonElement row, string prop)
    {
        if (!row.TryGetProperty(prop, out var el))
        {
            return null;
        }

        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n) && n > 0)
        {
            return n;
        }

        if (el.ValueKind == JsonValueKind.String &&
            int.TryParse(el.GetString(), out var parsed) &&
            parsed > 0)
        {
            return parsed;
        }

        return null;
    }

    private static bool ParseBool(JsonElement row, string prop)
    {
        if (!row.TryGetProperty(prop, out var el))
        {
            return false;
        }

        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => el.GetString() is "1" or "true" or "True" or "yes" or "sí" or "si",
            JsonValueKind.Number => el.TryGetInt32(out var n) && n != 0,
            _ => false,
        };
    }

    private static string ParseEffect(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString()?.Trim() ?? "";
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            return GetString(value, "effect") ?? GetString(value, "action") ?? "";
        }

        return "";
    }

    private static List<string> CollectNames(JsonElement row)
    {
        var names = new List<string>();
        Push(names, GetString(row, "gift"));
        Push(names, GetString(row, "name"));
        PushArray(names, row, "also");
        PushArray(names, row, "names");
        Push(names, GetString(row, "id"));
        PushArray(names, row, "ids");
        return names;
    }

    private static void Push(List<string> names, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            names.Add(value.Trim());
        }
    }

    private static void PushArray(List<string> names, JsonElement row, string prop)
    {
        if (!row.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                Push(names, item.GetString());
            }
            else if (item.ValueKind is JsonValueKind.Number)
            {
                Push(names, item.ToString());
            }
        }
    }

    private static List<DiamondBracket> ParseDiamondBrackets(JsonElement root)
    {
        var list = new List<DiamondBracket>();
        if (!root.TryGetProperty("giftsByDiamonds", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var row in arr.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            list.Add(new DiamondBracket
            {
                Min = row.TryGetProperty("min", out var min) && min.TryGetInt32(out var minV) ? minV : 0,
                Max = row.TryGetProperty("max", out var max) && max.TryGetInt32(out var maxV) ? maxV : null,
                Effect = GetString(row, "effect") ?? "",
            });
        }

        return list;
    }

    private static List<BitsBracket> ParseBitsBrackets(JsonElement container)
    {
        var list = new List<BitsBracket>();
        if (!container.TryGetProperty("bits", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var row in arr.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var plan = ParsePlan(row, GetString(row, "effect") ?? "", null);
            if (plan.IsEmpty)
            {
                continue;
            }

            var min = row.TryGetProperty("min", out var minEl) && minEl.TryGetInt32(out var minV)
                ? Math.Max(1, minV)
                : 1;
            list.Add(new BitsBracket { Min = min, Effect = plan.EffectId, Plan = plan });
        }

        return list;
    }

    private static List<GiftGoalConfig> ParseGoals(JsonElement root)
    {
        var list = new List<GiftGoalConfig>();
        if (!root.TryGetProperty("goals", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var row in arr.EnumerateArray())
        {
            index++;
            if (row.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var plan = ParsePlan(row, GetString(row, "effect") ?? "", null);
            var effect = plan.EffectId;
            var keys = new List<string>();
            PushStrings(keys, row, "gifts");
            PushStrings(keys, row, "ids");
            var uniqueKeys = keys
                .Select(GiftKeyNormalizer.Normalize)
                .Where(k => k.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (uniqueKeys.Count == 0 || string.IsNullOrWhiteSpace(effect))
            {
                continue;
            }

            var label = GetString(row, "label") ?? GetString(row, "name") ?? uniqueKeys[0];
            var id = GiftKeyNormalizer.Normalize(GetString(row, "id"));
            if (string.IsNullOrWhiteSpace(id))
            {
                id = GiftKeyNormalizer.Normalize(label);
            }

            if (string.IsNullOrWhiteSpace(id))
            {
                id = $"meta-{index}";
            }

            var baseId = id;
            var n = 2;
            while (!usedIds.Add(id))
            {
                id = $"{baseId}-{n}";
                n++;
            }

            var need = row.TryGetProperty("need", out var needEl) && needEl.TryGetInt32(out var needV)
                ? Math.Max(1, needV)
                : 2;
            var alsoInstant = !row.TryGetProperty("alsoInstant", out var ai) || ParseBool(ai);
            var repeat = !row.TryGetProperty("repeat", out var rp) || ParseBool(rp);

            list.Add(new GiftGoalConfig
            {
                Id = id,
                Label = string.IsNullOrWhiteSpace(label) ? id : label.Trim(),
                Need = need,
                Effect = effect.Trim(),
                Plan = plan,
                Keys = uniqueKeys,
                AlsoInstant = alsoInstant,
                Repeat = repeat,
            });
        }

        return list;
    }

    private static void PushStrings(List<string> target, JsonElement row, string prop)
    {
        if (!row.TryGetProperty(prop, out var el))
        {
            return;
        }

        if (el.ValueKind == JsonValueKind.String)
        {
            var s = el.GetString();
            if (!string.IsNullOrWhiteSpace(s))
            {
                target.Add(s);
            }

            return;
        }

        if (el.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in el.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var s = item.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                {
                    target.Add(s);
                }
            }
        }
    }

    private static bool ParseBool(JsonElement el) =>
        el.ValueKind == JsonValueKind.True ||
        (el.ValueKind == JsonValueKind.String &&
         (string.Equals(el.GetString(), "true", StringComparison.OrdinalIgnoreCase) ||
          el.GetString() == "1"));

    private static LikesConfig ParseLikes(JsonElement root)
    {
        if (!root.TryGetProperty("likes", out var likes) || likes.ValueKind != JsonValueKind.Object)
        {
            return new LikesConfig();
        }

        var plan = ParsePlan(likes, GetString(likes, "effect") ?? "", null);
        return new LikesConfig
        {
            Every = likes.TryGetProperty("every", out var every) && every.TryGetInt32(out var n) ? n : 0,
            Effect = plan.EffectId,
            Plan = plan,
        };
    }

    private static SimpleEffectConfig ParseSimple(JsonElement root, string prop)
    {
        if (!root.TryGetProperty(prop, out var el) || el.ValueKind != JsonValueKind.Object)
        {
            return new SimpleEffectConfig();
        }

        var plan = ParsePlan(el, GetString(el, "effect") ?? "", null);
        return new SimpleEffectConfig
        {
            Effect = plan.EffectId,
            Plan = plan,
            OncePerUser = !el.TryGetProperty("oncePerUser", out _) || ParseBool(el, "oncePerUser"),
        };
    }

    private static ChatConfig ParseChat(JsonElement root)
    {
        if (!root.TryGetProperty("chat", out var chat) || chat.ValueKind != JsonValueKind.Object)
        {
            return new ChatConfig();
        }

        var commands = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var plans = new Dictionary<string, EffectPlan>(StringComparer.OrdinalIgnoreCase);
        if (chat.TryGetProperty("commands", out var cmds) && cmds.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in cmds.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.String)
                {
                    commands[p.Name] = p.Value.GetString() ?? "";
                }
                else if (p.Value.ValueKind == JsonValueKind.Object)
                {
                    // Un comando con varios efectos: { "effect": "a", "effects": [ ... ] }.
                    var plan = ParsePlan(p.Value, GetString(p.Value, "effect") ?? "", null);
                    if (!plan.IsEmpty)
                    {
                        commands[p.Name] = plan.EffectId;
                        plans[p.Name] = plan;
                    }
                }
            }
        }

        return new ChatConfig
        {
            Enabled = chat.TryGetProperty("enabled", out var en) && en.ValueKind == JsonValueKind.True,
            Prefix = GetString(chat, "prefix") ?? "!",
            CooldownSec = ReadWaitSec(chat, "cooldownSec", 30),
            GlobalGapSec = ReadWaitSec(chat, "globalGapSec", 2),
            Commands = commands,
            Plans = plans,
        };
    }

    private static int ReadWaitSec(JsonElement obj, string name, int fallback)
    {
        if (!obj.TryGetProperty(name, out var el))
        {
            return fallback;
        }

        var n = el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetInt32(out var i) => i,
            JsonValueKind.String when int.TryParse(el.GetString(), out var s) => s,
            _ => fallback,
        };
        return Math.Clamp(n, 0, 3600);
    }

    private static string? GetString(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p))
        {
            return null;
        }

        return p.ValueKind switch
        {
            JsonValueKind.String => p.GetString(),
            JsonValueKind.Number => p.ToString(),
            _ => null,
        };
    }
}
