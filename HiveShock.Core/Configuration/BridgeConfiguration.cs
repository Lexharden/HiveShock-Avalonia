using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace HiveShock.Configuration;

public sealed class BridgeOptions
{
    public string TikTokUniqueId { get; set; } = "";
    public string? TikTokTtwid { get; set; }
    public string? TikTokCookies { get; set; }
    public string GameHost { get; set; } = "127.0.0.1";
    public int GamePort { get; set; } = 43000;
    public int CrowdControlPort { get; set; } = 43001;
    /// <summary>Puerto donde el bridge escucha eventos del juego (muertes, save borrado).</summary>
    public int EventPort { get; set; } = 43002;
    public bool DryRun { get; set; }
    public bool DisableCrowdControl { get; set; }
    public bool ListEffectsOnly { get; set; }
    public bool SkipMenu { get; set; }
    /// <summary>Solo pruebas / SDK → juego. No conecta a TikTok Live.</summary>
    public bool DevMode { get; set; }
    /// <summary>Conecta a TikTok solo para capturar regalos al catálogo (sin efectos ni CC).</summary>
    public bool CaptureOnly { get; set; }
    public TimeSpan LiveRetryInterval { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan GameConnectTimeout { get; set; } = TimeSpan.FromSeconds(3);
    /// <summary>Id de perfil preferido (--profile / CROWDBRIDGE_PROFILE). Vacío = active-profile.txt.</summary>
    public string? ProfileId { get; set; }

    public static BridgeOptions FromEnvironmentAndArgs(string[] args)
    {
        EnvFileLoader.Load(AppPaths.Find(".env"));

        var dry = HasFlag(args, "--dry") || EnvBool("TIKTOK_DRY_RUN");
        var noCc = HasFlag(args, "--no-cc") || EnvBool("DISABLE_CC");
        var list = HasFlag(args, "--list");
        var skipMenu = HasFlag(args, "--start") || HasFlag(args, "--no-menu");
        var dev = HasFlag(args, "--dev") || HasFlag(args, "--sdk") || EnvBool("BRIDGE_DEV_MODE");
        var tiktok = NormalizeUniqueId(ArgValue(args, "--tiktok") ?? Env("TIKTOK_UNIQUE_ID"));
        var profile = ArgValue(args, "--profile")
                      ?? Env("CROWDBRIDGE_PROFILE")
                      ?? Env("BRIDGE_PROFILE");

        return new BridgeOptions
        {
            TikTokUniqueId = tiktok,
            TikTokTtwid = Env("TIKTOK_TTWID"),
            TikTokCookies = Env("TIKTOK_COOKIES"),
            GameHost = Env("GAME_HOST") ?? "127.0.0.1",
            GamePort = EnvInt("GAME_PORT", 43000),
            CrowdControlPort = EnvInt("CC_PORT", 43001),
            EventPort = EnvInt("EVENT_PORT", 43002),
            DryRun = dry,
            DisableCrowdControl = noCc,
            ListEffectsOnly = list,
            SkipMenu = skipMenu,
            DevMode = dev,
            ProfileId = string.IsNullOrWhiteSpace(profile) ? null : profile.Trim(),
        };
    }

    /// <summary>Aplica host/puertos del perfil (sin pisar overrides explícitos de .env si ya diferían del default del perfil anterior).</summary>
    public void ApplyProfileEndpoints(GameProfileInfo profile, bool force = true)
    {
        if (force || string.IsNullOrWhiteSpace(GameHost))
        {
            GameHost = string.IsNullOrWhiteSpace(profile.GameHost) ? "127.0.0.1" : profile.GameHost;
        }

        if (force)
        {
            GamePort = profile.GamePort > 0 ? profile.GamePort : 43000;
            CrowdControlPort = profile.CrowdControlPort > 0 ? profile.CrowdControlPort : 43001;
            EventPort = profile.EventPort > 0 ? profile.EventPort : 43002;
        }
    }

    public static string NormalizeUniqueId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var text = value.Trim();
        text = Regex.Replace(text, @"^https?://(www\.)?tiktok\.com/@", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"/live/?$", "", RegexOptions.IgnoreCase);
        text = text.TrimStart('@');
        var cut = text.IndexOfAny(['/', '?', '#']);
        if (cut >= 0)
        {
            text = text[..cut];
        }

        return text.Trim();
    }

    private static bool HasFlag(string[] args, string flag) =>
        args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

    private static string? ArgValue(string[] args, string flag)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static string? Env(string key)
    {
        var value = Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static int EnvInt(string key, int fallback) =>
        int.TryParse(Env(key), out var n) ? n : fallback;

    private static bool EnvBool(string key)
    {
        var value = Env(key);
        return value is "1" or "true" or "TRUE" or "yes" or "YES";
    }
}

public static class AppPaths
{
    /// <summary>Directory that holds the published exe (not the single-file extract folder).</summary>
    public static string AppDirectory
    {
        get
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath))
            {
                var dir = Path.GetDirectoryName(processPath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    return dir;
                }
            }

            return AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    public static string? Find(string fileName)
    {
        foreach (var root in CandidateRoots())
        {
            var path = Path.Combine(root, fileName);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    public static string Require(string fileName) =>
        Find(fileName) ?? throw new FileNotFoundException(
            $"No se encontró {fileName}. Debe estar junto a {ProductInfo.ExecutableFileName} (carpeta: {AppDirectory}).");

    public static IEnumerable<string> CandidateRootsPublic() => CandidateRoots();

    private static IEnumerable<string> CandidateRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in EnumerateRootCandidates())
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            string full;
            try
            {
                full = Path.GetFullPath(raw);
            }
            catch
            {
                continue;
            }

            if (!seen.Add(full))
            {
                continue;
            }

            yield return full;
        }
    }

    private static IEnumerable<string> EnumerateRootCandidates()
    {
        yield return AppDirectory;
        yield return AppContext.BaseDirectory;
        yield return Directory.GetCurrentDirectory();

        foreach (var macRoot in EnumerateMacBundleRoots(AppDirectory))
        {
            yield return macRoot;
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir != null; i++)
        {
            yield return dir.FullName;
            dir = dir.Parent;
        }
    }

    /// <summary>
    /// HiveShock.app/Contents/MacOS → Resources y Contents, para datos junto al bundle.
    /// </summary>
    private static IEnumerable<string> EnumerateMacBundleRoots(string appDirectory)
    {
        DirectoryInfo? macOs;
        try
        {
            macOs = new DirectoryInfo(appDirectory);
        }
        catch
        {
            yield break;
        }

        if (!string.Equals(macOs.Name, "MacOS", StringComparison.Ordinal) ||
            macOs.Parent is not { Name: "Contents" } contents)
        {
            yield break;
        }

        yield return Path.Combine(contents.FullName, "Resources");
        yield return contents.FullName;
        if (contents.Parent != null)
        {
            yield return contents.Parent.FullName;
        }
    }
}

public static class EnvFileLoader
{
    public static void Load(string? envPath)
    {
        if (string.IsNullOrWhiteSpace(envPath) || !File.Exists(envPath))
        {
            return;
        }

        foreach (var raw in File.ReadAllLines(envPath, Encoding.UTF8))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if ((value.StartsWith('"') && value.EndsWith('"')) ||
                (value.StartsWith('\'') && value.EndsWith('\'')))
            {
                value = value[1..^1];
            }

            if (Environment.GetEnvironmentVariable(key) is null)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }
}

public static class GiftKeyNormalizer
{
    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "";
        }

        var formD = name.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var ch in formD)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}

public sealed record GiftRule(
    string Key,
    string Normalized,
    string EffectId,
    string What,
    int MinCount = 1,
    bool Each = false);

public sealed record GiftGroup(IReadOnlyList<string> Names, string EffectId, string What);

/// <summary>Resultado de resolver un regalo TikTok a un efecto + reglas de combo.</summary>
public sealed class GiftMatch
{
    public string EffectId { get; init; } = "";
    /// <summary>Mínimo de unidades del combo (xN) para disparar. 1 = siempre.</summary>
    public int MinCount { get; init; } = 1;
    /// <summary>Si true, encola el efecto una vez por unidad (x10 → 10). Si false, una sola vez al cerrar el combo.</summary>
    public bool Each { get; init; }
}

public sealed class DiamondBracket
{
    public int Min { get; init; }
    public int? Max { get; init; }
    public string Effect { get; init; } = "";
}

public sealed class LikesConfig
{
    public int Every { get; init; }
    public string Effect { get; init; } = "";
}

public sealed class SimpleEffectConfig
{
    public string Effect { get; init; } = "";
}

public sealed class ChatConfig
{
    public bool Enabled { get; init; }
    public string Prefix { get; init; } = "!";
    public Dictionary<string, string> Commands { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class GiftRuntimeConfig
{
    public IReadOnlyList<GiftRule> Rules { get; init; } = [];
    public IReadOnlyList<GiftGroup> Groups { get; init; } = [];
    public string UnmappedGifts { get; init; } = "ignore";
    public IReadOnlyList<DiamondBracket> GiftsByDiamonds { get; init; } = [];
    public LikesConfig Likes { get; init; } = new();
    public SimpleEffectConfig Follow { get; init; } = new();
    public SimpleEffectConfig Share { get; init; } = new();
    public ChatConfig Chat { get; init; } = new();
}

public sealed class EffectCatalog
{
    private readonly Dictionary<string, Dictionary<string, JsonElement>> _effects;
    private readonly Dictionary<string, string> _labels;
    private readonly HashSet<string> _enemyEffectIds;
    private readonly HashSet<string> _singletonEffectIds;

    public EffectCatalog(
        Dictionary<string, Dictionary<string, JsonElement>> effects,
        IReadOnlyDictionary<string, string>? labels = null,
        IEnumerable<string>? enemyEffects = null,
        IEnumerable<string>? singletonEffects = null)
    {
        _effects = effects;
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

    public static EffectCatalog Load(string path, GameProfileInfo? profile = null)
    {
        var json = File.ReadAllText(path, Encoding.UTF8);
        var parsed = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, JsonElement>>>(
            json,
            JsonDefaults.Options) ?? new();
        return new EffectCatalog(
            parsed,
            profile?.Labels,
            profile?.EnemyEffects,
            profile?.SingletonEffects);
    }

    public IEnumerable<string> Ids => _effects.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase);

    public int Count => _effects.Count;

    public bool Contains(string effectId) =>
        !string.IsNullOrWhiteSpace(effectId) && _effects.ContainsKey(effectId);

    public bool IsSingletonEffect(string effectId) =>
        _singletonEffectIds.Contains(effectId);

    public string? GetAction(string effectId)
    {
        if (!_effects.TryGetValue(effectId, out var template))
        {
            return null;
        }

        if (template.TryGetValue("action", out var action) && action.ValueKind == JsonValueKind.String)
        {
            return action.GetString();
        }

        return null;
    }

    public string Describe(string effectId)
    {
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

    public Dictionary<string, object?>? Resolve(string effectId, string? user)
    {
        if (string.IsNullOrWhiteSpace(effectId) || effectId.StartsWith('_'))
        {
            return null;
        }

        if (!_effects.TryGetValue(effectId, out var template))
        {
            Console.Error.WriteLine($"[{ProductInfo.Name}] Efecto desconocido: {effectId}");
            return null;
        }

        var cmd = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in template)
        {
            cmd[key] = JsonElementToObject(value);
        }

        if (!string.IsNullOrWhiteSpace(user))
        {
            cmd["user"] = user;
        }

        return cmd;
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

public sealed class GiftConfigStore
{
    private static readonly HashSet<string> MetaKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "_help", "_comment", "gifts", "likes", "follow", "share", "chat", "unmappedGifts", "giftsByDiamonds",
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
                return new GiftMatch { EffectId = row.Effect, MinCount = 1, Each = false };
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
                var names = CollectNames(row);
                var groupNames = new List<string>();
                foreach (var name in names)
                {
                    if (TryAddRule(rules, name, effectId, what, minCount, each))
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
                if (TryAddRule(rules, prop.Name, effectId, what, minCount, each))
                {
                    groups.Add(new GiftGroup([prop.Name], effectId, what));
                }
            }
        }

        return new GiftRuntimeConfig
        {
            Rules = rules,
            Groups = groups,
            UnmappedGifts = GetString(root, "unmappedGifts") ?? "ignore",
            GiftsByDiamonds = ParseDiamondBrackets(root),
            Likes = ParseLikes(root),
            Follow = ParseSimple(root, "follow"),
            Share = ParseSimple(root, "share"),
            Chat = ParseChat(root),
        };
    }

    private bool TryAddRule(
        List<GiftRule> rules,
        string key,
        string effectId,
        string what,
        int minCount = 1,
        bool each = false)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(effectId))
        {
            return false;
        }

        if (!_effects.Contains(effectId))
        {
            Console.Error.WriteLine($"[gifts.json] \"{key}\" apunta a un efecto que no existe: {effectId}");
            return false;
        }

        rules.Add(new GiftRule(
            key,
            GiftKeyNormalizer.Normalize(key),
            effectId,
            what.Trim(),
            Math.Max(1, minCount),
            each));
        return true;
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

    private static LikesConfig ParseLikes(JsonElement root)
    {
        if (!root.TryGetProperty("likes", out var likes) || likes.ValueKind != JsonValueKind.Object)
        {
            return new LikesConfig();
        }

        return new LikesConfig
        {
            Every = likes.TryGetProperty("every", out var every) && every.TryGetInt32(out var n) ? n : 0,
            Effect = GetString(likes, "effect") ?? "",
        };
    }

    private static SimpleEffectConfig ParseSimple(JsonElement root, string prop)
    {
        if (!root.TryGetProperty(prop, out var el) || el.ValueKind != JsonValueKind.Object)
        {
            return new SimpleEffectConfig();
        }

        return new SimpleEffectConfig { Effect = GetString(el, "effect") ?? "" };
    }

    private static ChatConfig ParseChat(JsonElement root)
    {
        if (!root.TryGetProperty("chat", out var chat) || chat.ValueKind != JsonValueKind.Object)
        {
            return new ChatConfig();
        }

        var commands = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (chat.TryGetProperty("commands", out var cmds) && cmds.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in cmds.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.String)
                {
                    commands[p.Name] = p.Value.GetString() ?? "";
                }
            }
        }

        return new ChatConfig
        {
            Enabled = chat.TryGetProperty("enabled", out var en) && en.ValueKind == JsonValueKind.True,
            Prefix = GetString(chat, "prefix") ?? "!",
            Commands = commands,
        };
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

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };
}
