using System.Text.Json;
using HiveShock.Configuration;
using HiveShock.Live;

namespace HiveShock.Tests.Configuration;

public sealed class EventPlansTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hiveshock-events-" + Guid.NewGuid().ToString("N"));

    public EventPlansTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static EffectCatalog Catalog()
    {
        var map = new Dictionary<string, Dictionary<string, JsonElement>>();
        foreach (var id in new[] { "a", "b", "c" })
        {
            map[id] = JsonDocument.Parse($$"""{ "action": "{{id}}" }""").RootElement
                .EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
        }

        return new EffectCatalog(map);
    }

    private string Write(string json)
    {
        var path = Path.Combine(_dir, "gifts.json");
        File.WriteAllText(path, json);
        return path;
    }

    private const string Everything = """
        {
          "gifts": [],
          "likes": { "every": 50, "effect": "a", "effects": [ "a", { "effect": "b", "delaySec": 2 } ] },
          "follow": { "effect": "a", "effects": [ "a", "b", "c" ] },
          "share": { "effect": "c" },
          "chat": { "enabled": true, "prefix": "!", "commands": {
              "uno": "a",
              "dos": { "effect": "a", "effects": [ "a", { "effect": "c", "delaySec": 1.5 } ] } } },
          "twitch": {
            "follow": { "effect": "b", "effects": [ "b", "c" ] },
            "bits": [ { "min": 100, "effect": "a", "effects": [ "a", "b" ] }, { "min": 1, "effect": "c" } ]
          },
          "goals": [ { "id": "g", "label": "Donas", "need": 3, "gifts": ["Doughnut"], "effect": "a", "effects": [ "a", "b" ] } ]
        }
        """;

    [Fact]
    public void Every_trigger_reads_its_list_of_effects_and_a_single_effect_stays_a_plan_of_one()
    {
        var store = new GiftConfigStore(Catalog());
        store.Reload(Write(Everything));
        var cfg = store.Snapshot;

        Assert.Equal(["a", "b"], cfg.Likes.EffectivePlan.Steps.Select(s => s.EffectId));
        Assert.Equal(2000, cfg.Likes.EffectivePlan.Steps[1].DelayMs);
        Assert.Equal(["a", "b", "c"], cfg.Follow.EffectivePlan.Steps.Select(s => s.EffectId));
        Assert.Equal(["c"], cfg.Share.EffectivePlan.Steps.Select(s => s.EffectId));
        Assert.Equal(["b", "c"], cfg.TwitchFollow.EffectivePlan.Steps.Select(s => s.EffectId));

        Assert.Equal("a", cfg.Chat.Commands["uno"]);
        Assert.Equal(["a"], cfg.Chat.PlanFor("uno", cfg.Chat.Commands["uno"]).Steps.Select(s => s.EffectId));
        Assert.Equal("a", cfg.Chat.Commands["dos"]);
        var dos = cfg.Chat.PlanFor("dos", "a");
        Assert.Equal(["a", "c"], dos.Steps.Select(s => s.EffectId));
        Assert.Equal(1500, dos.Steps[1].DelayMs);

        var bits = cfg.TwitchBits.OrderByDescending(b => b.Min).ToList();
        Assert.Equal(["a", "b"], bits[0].EffectivePlan.Steps.Select(s => s.EffectId));
        Assert.Equal(["c"], bits[1].EffectivePlan.Steps.Select(s => s.EffectId));

        Assert.Equal(["a", "b"], cfg.Goals[0].EffectivePlan.Steps.Select(s => s.EffectId));
    }

    [Fact]
    public void A_goal_that_fires_carries_all_its_effects()
    {
        var store = new GiftConfigStore(Catalog());
        store.Reload(Write(Everything));
        var bank = new GiftGoalBank();
        bank.ApplyDefinitions(store.Snapshot.Goals);

        var tick = bank.Contribute("Doughnut", null, 3);

        Assert.NotNull(tick);
        Assert.True(tick.Fired);
        Assert.Equal(["a", "b"], tick.Plan.Steps.Select(s => s.EffectId));
    }

    [Fact]
    public void Saving_without_extras_keeps_the_old_format_and_extras_round_trip()
    {
        var path = Write(Everything);
        var editor = new GiftFileEditor(path);
        editor.Load();

        Assert.Equal("a", editor.FollowEffect);
        Assert.Equal(["b", "c"], editor.FollowMore.Select(s => s.Effect));
        Assert.Equal(2, editor.LikesMore.Count + 1);
        Assert.Equal("a", editor.ChatCommands.First(c => c.Word == "dos").Effect);
        Assert.Equal(1.5, editor.ChatCommands.First(c => c.Word == "dos").MoreSteps[0].DelaySec);
        Assert.Empty(editor.ChatCommands.First(c => c.Word == "uno").MoreSteps);
        Assert.Equal(["b"], editor.TwitchBits.First(b => b.Min == 100).MoreSteps.Select(s => s.Effect));
        Assert.Equal(["b"], editor.Goals[0].MoreSteps.Select(s => s.Effect));

        editor.Save();
        var again = new GiftFileEditor(path);
        again.Load();
        Assert.Equal(["b", "c"], again.FollowMore.Select(s => s.Effect));
        Assert.Equal(["c"], again.ChatCommands.First(c => c.Word == "dos").MoreSteps.Select(s => s.Effect));
        Assert.Equal(["c"], again.TwitchFollowMore.Select(s => s.Effect));

        // Quitar los extras devuelve el archivo al formato de siempre: sin «effects» y el comando como texto.
        editor.FollowMore.Clear();
        editor.ChatCommands.First(c => c.Word == "dos").MoreSteps.Clear();
        editor.Goals[0].MoreSteps.Clear();
        editor.Save();

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        Assert.False(root.GetProperty("follow").TryGetProperty("effects", out _));
        Assert.Equal(JsonValueKind.String, root.GetProperty("chat").GetProperty("commands").GetProperty("dos").ValueKind);
        Assert.False(root.GetProperty("goals")[0].TryGetProperty("effects", out _));
        Assert.True(root.GetProperty("likes").TryGetProperty("effects", out _)); // el que no se tocó se conserva
    }

    [Fact]
    public void The_engine_reads_what_the_editor_saved()
    {
        var path = Write(Everything);
        var editor = new GiftFileEditor(path);
        editor.Load();
        editor.ShareMore.Add(new EditableGiftStep { Effect = "a", DelaySec = 3 });
        editor.Save();

        var store = new GiftConfigStore(Catalog());
        store.Reload(path);

        Assert.Equal(["c", "a"], store.Snapshot.Share.EffectivePlan.Steps.Select(s => s.EffectId));
        Assert.Equal(3000, store.Snapshot.Share.EffectivePlan.Steps[1].DelayMs);
    }
}
