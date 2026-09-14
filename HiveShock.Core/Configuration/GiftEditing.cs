using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HiveShock.Configuration;

public sealed class EditableGift
{
    public string Gift { get; set; } = "";
    public List<string> Also { get; set; } = [];
    public string Id { get; set; } = "";
    public string Effect { get; set; } = "";
    public string What { get; set; } = "";
    public int? Diamonds { get; set; }
    /// <summary>Mínimo de unidades del combo (xN) para disparar. 1 = siempre.</summary>
    public int MinCount { get; set; } = 1;
    /// <summary>Si true, un efecto por unidad (x10 → 10). Si false, una vez al cerrar el combo.</summary>
    public bool Each { get; set; }
    /// <summary>Ruta opcional en gifts.json (ej. 5655.webp o gifts-images/custom.png).</summary>
    public string Image { get; set; } = "";
    /// <summary>Si true, aparece en la ventana overlay de OBS / live.</summary>
    public bool Overlay { get; set; }
    /// <summary>Texto en el overlay. Vacío = nombre del gift.</summary>
    public string OverlayText { get; set; } = "";

    public string OverlayLabel =>
        string.IsNullOrWhiteSpace(OverlayText) ? Gift : OverlayText;

    public string? ResolvedImagePath
    {
        get
        {
            var direct = GiftImages.ResolvePath(Id, Gift, Image);
            if (direct != null)
            {
                return direct;
            }

            foreach (var alias in Also)
            {
                var byAlias = GiftImages.ResolvePath(null, alias, null);
                if (byAlias != null)
                {
                    return byAlias;
                }
            }

            return null;
        }
    }

    public bool HasImage => ResolvedImagePath != null;
}

public sealed class EditableChatCommand
{
    public string Word { get; set; } = "";
    public string Effect { get; set; } = "";
}

public sealed class EditableBitsRule
{
    public int Min { get; set; } = 1;
    public string Effect { get; set; } = "";
}

public sealed class EditableGiftGoal
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public int Need { get; set; } = 2;
    public string Effect { get; set; } = "";
    public string GiftsText { get; set; } = "";
    public bool AlsoInstant { get; set; } = true;
    public bool Repeat { get; set; } = true;
}

public sealed class GiftFileEditor
{
    private readonly string _path;
    private JsonObject _root = new();
    private List<EditableGift> _gifts = [];

    public GiftFileEditor(string path) => _path = path;

    public IReadOnlyList<EditableGift> Gifts => _gifts;

    public int LikesEvery { get; set; }
    public string LikesEffect { get; set; } = "";
    public string FollowEffect { get; set; } = "";
    public string ShareEffect { get; set; } = "";
    public bool ChatEnabled { get; set; }
    public string ChatPrefix { get; set; } = "!";
    public int ChatCooldownSec { get; set; } = 30;
    public int ChatGlobalGapSec { get; set; } = 2;
    public List<EditableChatCommand> ChatCommands { get; } = [];
    public bool TwitchChatEnabled { get; set; }
    public string TwitchChatPrefix { get; set; } = "!";
    public int TwitchChatCooldownSec { get; set; } = 30;
    public int TwitchChatGlobalGapSec { get; set; } = 2;
    public List<EditableChatCommand> TwitchChatCommands { get; } = [];
    public string TwitchFollowEffect { get; set; } = "";
    public List<EditableBitsRule> TwitchBits { get; } = [];
    public List<EditableGiftGoal> Goals { get; } = [];

    public void Load()
    {
        var text = File.ReadAllText(_path, Encoding.UTF8);
        _root = JsonNode.Parse(text, new JsonNodeOptions { PropertyNameCaseInsensitive = true }) as JsonObject
                ?? throw new InvalidDataException("gifts.json inválido");

        _gifts = [];
        if (_root["gifts"] is JsonArray arr)
        {
            foreach (var node in arr)
            {
                if (node is JsonObject obj)
                {
                    _gifts.Add(FromNode(obj));
                }
            }
        }

        LoadLiveEvents();
    }

    public void Save()
    {
        var arr = new JsonArray();
        foreach (var gift in _gifts)
        {
            arr.Add(ToNode(gift));
        }

        _root["gifts"] = arr;
        SaveLiveEvents();
        var json = _root.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        File.WriteAllText(_path, json + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private void LoadLiveEvents()
    {
        LikesEvery = 0;
        LikesEffect = "";
        FollowEffect = "";
        ShareEffect = "";
        ChatEnabled = false;
        ChatPrefix = "!";
        ChatCooldownSec = 30;
        ChatGlobalGapSec = 2;
        ChatCommands.Clear();
        TwitchChatEnabled = false;
        TwitchChatPrefix = "!";
        TwitchChatCooldownSec = 30;
        TwitchChatGlobalGapSec = 2;
        TwitchChatCommands.Clear();
        TwitchFollowEffect = "";
        TwitchBits.Clear();
        Goals.Clear();

        if (_root["likes"] is JsonObject likes)
        {
            LikesEvery = ReadInt(likes, "every") ?? 0;
            LikesEffect = likes["effect"]?.GetValue<string>() ?? "";
        }

        if (_root["follow"] is JsonObject follow)
        {
            FollowEffect = follow["effect"]?.GetValue<string>() ?? "";
        }

        if (_root["share"] is JsonObject share)
        {
            ShareEffect = share["effect"]?.GetValue<string>() ?? "";
        }

        if (_root["chat"] is JsonObject chat)
        {
            ChatEnabled = ReadBool(chat, "enabled");
            ChatPrefix = chat["prefix"]?.GetValue<string>() ?? "!";
            ChatCooldownSec = ClampWaitSec(ReadInt(chat, "cooldownSec") ?? 30);
            ChatGlobalGapSec = ClampWaitSec(ReadInt(chat, "globalGapSec") ?? 2);
            if (chat["commands"] is JsonObject cmds)
            {
                foreach (var kv in cmds)
                {
                    var effect = kv.Value?.GetValue<string>() ?? "";
                    ChatCommands.Add(new EditableChatCommand { Word = kv.Key, Effect = effect });
                }
            }
        }

        if (_root["twitch"] is JsonObject twitch)
        {
            if (twitch["follow"] is JsonObject twFollow)
            {
                TwitchFollowEffect = twFollow["effect"]?.GetValue<string>() ?? "";
            }

            if (twitch["chat"] is JsonObject twChat)
            {
                TwitchChatEnabled = ReadBool(twChat, "enabled");
                TwitchChatPrefix = twChat["prefix"]?.GetValue<string>() ?? "!";
                TwitchChatCooldownSec = ClampWaitSec(ReadInt(twChat, "cooldownSec") ?? 30);
                TwitchChatGlobalGapSec = ClampWaitSec(ReadInt(twChat, "globalGapSec") ?? 2);
                if (twChat["commands"] is JsonObject twCmds)
                {
                    foreach (var kv in twCmds)
                    {
                        TwitchChatCommands.Add(new EditableChatCommand
                        {
                            Word = kv.Key,
                            Effect = kv.Value?.GetValue<string>() ?? "",
                        });
                    }
                }
            }

            if (twitch["bits"] is JsonArray twBits)
            {
                foreach (var node in twBits)
                {
                    if (node is not JsonObject bit)
                    {
                        continue;
                    }

                    TwitchBits.Add(new EditableBitsRule
                    {
                        Min = Math.Max(1, ReadInt(bit, "min") ?? 1),
                        Effect = bit["effect"]?.GetValue<string>() ?? "",
                    });
                }
            }
        }
        else
        {
            TwitchFollowEffect = FollowEffect;
            TwitchChatEnabled = ChatEnabled;
            TwitchChatPrefix = ChatPrefix;
            TwitchChatCooldownSec = ChatCooldownSec;
            TwitchChatGlobalGapSec = ChatGlobalGapSec;
            foreach (var cmd in ChatCommands)
            {
                TwitchChatCommands.Add(new EditableChatCommand { Word = cmd.Word, Effect = cmd.Effect });
            }
        }

        if (_root["goals"] is JsonArray goals)
        {
            foreach (var node in goals)
            {
                if (node is not JsonObject g)
                {
                    continue;
                }

                var keys = new List<string>();
                PushStrings(keys, g, "gifts");
                PushStrings(keys, g, "ids");
                Goals.Add(new EditableGiftGoal
                {
                    Id = g["id"]?.GetValue<string>() ?? "",
                    Label = g["label"]?.GetValue<string>() ?? g["name"]?.GetValue<string>() ?? "",
                    Need = Math.Max(1, ReadInt(g, "need") ?? 2),
                    Effect = g["effect"]?.GetValue<string>() ?? "",
                    GiftsText = string.Join(", ", keys),
                    AlsoInstant = ReadBoolOr(g, "alsoInstant", true),
                    Repeat = ReadBoolOr(g, "repeat", true),
                });
            }
        }
    }

    private void SaveLiveEvents()
    {
        var likes = _root["likes"] as JsonObject ?? new JsonObject();
        likes["every"] = LikesEvery;
        likes["effect"] = LikesEffect ?? "";
        _root["likes"] = likes;

        var follow = _root["follow"] as JsonObject ?? new JsonObject();
        follow["effect"] = FollowEffect ?? "";
        _root["follow"] = follow;

        var share = _root["share"] as JsonObject ?? new JsonObject();
        share["effect"] = ShareEffect ?? "";
        _root["share"] = share;

        var chat = _root["chat"] as JsonObject ?? new JsonObject();
        chat["enabled"] = ChatEnabled;
        chat["prefix"] = string.IsNullOrWhiteSpace(ChatPrefix) ? "!" : ChatPrefix.Trim();
        chat["cooldownSec"] = ClampWaitSec(ChatCooldownSec);
        chat["globalGapSec"] = ClampWaitSec(ChatGlobalGapSec);
        var cmds = new JsonObject();
        foreach (var cmd in ChatCommands)
        {
            if (string.IsNullOrWhiteSpace(cmd.Word) || string.IsNullOrWhiteSpace(cmd.Effect))
            {
                continue;
            }

            cmds[cmd.Word.Trim()] = cmd.Effect.Trim();
        }

        chat["commands"] = cmds;
        _root["chat"] = chat;

        var twitch = _root["twitch"] as JsonObject ?? new JsonObject();
        var twFollow = twitch["follow"] as JsonObject ?? new JsonObject();
        twFollow["effect"] = TwitchFollowEffect ?? "";
        twitch["follow"] = twFollow;
        var twChat = twitch["chat"] as JsonObject ?? new JsonObject();
        twChat["enabled"] = TwitchChatEnabled;
        twChat["prefix"] = string.IsNullOrWhiteSpace(TwitchChatPrefix) ? "!" : TwitchChatPrefix.Trim();
        twChat["cooldownSec"] = ClampWaitSec(TwitchChatCooldownSec);
        twChat["globalGapSec"] = ClampWaitSec(TwitchChatGlobalGapSec);
        var twCmds = new JsonObject();
        foreach (var cmd in TwitchChatCommands)
        {
            if (string.IsNullOrWhiteSpace(cmd.Word) || string.IsNullOrWhiteSpace(cmd.Effect))
            {
                continue;
            }

            twCmds[cmd.Word.Trim()] = cmd.Effect.Trim();
        }

        twChat["commands"] = twCmds;
        twitch["chat"] = twChat;
        var bitsArr = new JsonArray();
        foreach (var bit in TwitchBits.OrderBy(b => b.Min))
        {
            if (string.IsNullOrWhiteSpace(bit.Effect) || bit.Min < 1)
            {
                continue;
            }

            bitsArr.Add(new JsonObject
            {
                ["min"] = bit.Min,
                ["effect"] = bit.Effect.Trim(),
            });
        }

        twitch["bits"] = bitsArr;
        _root["twitch"] = twitch;

        var goalsArr = new JsonArray();
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var goal in Goals)
        {
            index++;
            if (string.IsNullOrWhiteSpace(goal.Effect) || goal.Need < 1)
            {
                continue;
            }

            var keys = SplitKeys(goal.GiftsText);
            if (keys.Count == 0)
            {
                continue;
            }

            var label = string.IsNullOrWhiteSpace(goal.Label) ? keys[0] : goal.Label.Trim();
            var id = GiftKeyNormalizer.Normalize(goal.Id);
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

            goalsArr.Add(new JsonObject
            {
                ["id"] = id,
                ["label"] = label,
                ["need"] = goal.Need,
                ["effect"] = goal.Effect.Trim(),
                ["gifts"] = ToJsonArray(keys),
                ["alsoInstant"] = goal.AlsoInstant,
                ["repeat"] = goal.Repeat,
            });
        }

        _root["goals"] = goalsArr;
    }

    private static List<string> SplitKeys(string? text) =>
        (text ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(s => s.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static JsonArray ToJsonArray(IEnumerable<string> keys)
    {
        var arr = new JsonArray();
        foreach (var key in keys)
        {
            arr.Add(key);
        }

        return arr;
    }

    private static void PushStrings(List<string> target, JsonObject obj, string key)
    {
        if (obj[key] is JsonValue scalar && scalar.TryGetValue<string>(out var one) &&
            !string.IsNullOrWhiteSpace(one))
        {
            target.Add(one.Trim());
            return;
        }

        if (obj[key] is not JsonArray arr)
        {
            return;
        }

        foreach (var node in arr)
        {
            if (node is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s))
            {
                target.Add(s.Trim());
            }
        }
    }

    private static int ClampWaitSec(int value) => Math.Clamp(value, 0, 3600);

    private static bool ReadBoolOr(JsonObject obj, string key, bool fallback) =>
        obj[key] is null ? fallback : ReadBool(obj, key);

    private static int? ReadInt(JsonObject obj, string key)
    {
        if (obj[key] is JsonValue v && v.TryGetValue<int>(out var n))
        {
            return n;
        }

        return null;
    }

    public void Add(EditableGift gift) => _gifts.Add(gift);

    public bool RemoveAt(int index)
    {
        if (index < 0 || index >= _gifts.Count)
        {
            return false;
        }

        _gifts.RemoveAt(index);
        return true;
    }

    private static EditableGift FromNode(JsonObject obj)
    {
        var also = new List<string>();
        if (obj["also"] is JsonArray alsoArr)
        {
            foreach (var item in alsoArr)
            {
                var s = item?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(s))
                {
                    also.Add(s);
                }
            }
        }

        int? diamonds = null;
        if (obj["diamonds"] is JsonValue dv && dv.TryGetValue<int>(out var d))
        {
            diamonds = d;
        }

        var minCount = 1;
        if (obj["minCount"] is JsonValue mv && mv.TryGetValue<int>(out var mc) && mc > 0)
        {
            minCount = mc;
        }
        else if (obj["min"] is JsonValue minv && minv.TryGetValue<int>(out var mn) && mn > 0)
        {
            minCount = mn;
        }

        var each = ReadBool(obj, "each") || ReadBool(obj, "perGift");

        return new EditableGift
        {
            Gift = obj["gift"]?.GetValue<string>() ?? obj["name"]?.GetValue<string>() ?? "",
            Also = also,
            Id = obj["id"]?.ToString()?.Trim('"') ?? "",
            Effect = obj["effect"]?.GetValue<string>() ?? "",
            What = obj["what"]?.GetValue<string>() ?? obj["note"]?.GetValue<string>() ?? "",
            Diamonds = diamonds,
            MinCount = minCount,
            Each = each,
            Image = obj["image"]?.GetValue<string>() ?? obj["img"]?.GetValue<string>() ?? "",
            Overlay = ReadBool(obj, "overlay"),
            OverlayText = obj["overlayText"]?.GetValue<string>() ?? obj["overlay_text"]?.GetValue<string>() ?? "",
        };
    }

    private static bool ReadBool(JsonObject obj, string key)
    {
        if (obj[key] is not JsonValue v)
        {
            return false;
        }

        if (v.TryGetValue<bool>(out var b))
        {
            return b;
        }

        if (v.TryGetValue<int>(out var n))
        {
            return n != 0;
        }

        if (v.TryGetValue<string>(out var s))
        {
            return s is "1" or "true" or "True" or "yes" or "sí" or "si";
        }

        return false;
    }

    private static JsonObject ToNode(EditableGift gift)
    {
        var obj = new JsonObject
        {
            ["gift"] = gift.Gift,
            ["effect"] = gift.Effect,
        };

        if (gift.Also.Count > 0)
        {
            var also = new JsonArray();
            foreach (var a in gift.Also)
            {
                also.Add(a);
            }

            obj["also"] = also;
        }

        if (!string.IsNullOrWhiteSpace(gift.Id))
        {
            obj["id"] = gift.Id;
        }

        if (gift.Diamonds is int diamonds)
        {
            obj["diamonds"] = diamonds;
        }

        if (gift.MinCount > 1)
        {
            obj["minCount"] = gift.MinCount;
        }

        if (gift.Each)
        {
            obj["each"] = true;
        }

        if (!string.IsNullOrWhiteSpace(gift.What))
        {
            obj["what"] = gift.What;
        }

        if (!string.IsNullOrWhiteSpace(gift.Image))
        {
            obj["image"] = gift.Image.Trim();
        }

        if (gift.Overlay)
        {
            obj["overlay"] = true;
        }

        if (!string.IsNullOrWhiteSpace(gift.OverlayText))
        {
            obj["overlayText"] = gift.OverlayText.Trim();
        }

        return obj;
    }
}

public static class EnvFileWriter
{
    public static void Upsert(string envPath, string key, string value)
    {
        var lines = File.Exists(envPath)
            ? File.ReadAllLines(envPath, Encoding.UTF8).ToList()
            : [];

        var found = false;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i].TrimStart();
            if (line.StartsWith('#') || !line.Contains('='))
            {
                continue;
            }

            var eq = lines[i].IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            if (!string.Equals(lines[i][..eq].Trim(), key, StringComparison.Ordinal))
            {
                continue;
            }

            lines[i] = $"{key}={value}";
            found = true;
            break;
        }

        if (!found)
        {
            if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
            {
                lines.Add("");
            }

            lines.Add($"{key}={value}");
        }

        File.WriteAllLines(envPath, lines, new UTF8Encoding(false));
        Environment.SetEnvironmentVariable(key, value);
    }

    public static string EnsureEnvPath()
    {
        var existing = AppPaths.Find(".env");
        if (existing != null)
        {
            return existing;
        }

        var path = Path.Combine(AppPaths.AppDirectory, ".env");
        var example = AppPaths.Find(".env.example");
        if (example != null)
        {
            // File.Copy usa ioctl FICLONE; Android lo deniega en app data (avc 0x9409).
            File.WriteAllBytes(path, File.ReadAllBytes(example));
        }
        else
        {
            File.WriteAllText(path, "TIKTOK_UNIQUE_ID=\n", new UTF8Encoding(false));
        }

        return path;
    }
}
