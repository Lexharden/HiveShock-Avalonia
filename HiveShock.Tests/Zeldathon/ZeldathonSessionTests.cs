using System.Text.Json;
using System.Text.Json.Nodes;
using HiveShock.Zeldathon;

namespace HiveShock.Tests.Zeldathon;

public class ZeldathonSessionTests
{
    private const string MapJson = """
        {
          "areas": { "85": "kokiri-forest", "81": "hyrule-field", "5": "water-temple" },
          "items": { "10": "hookshot", "3": "bow", "999": "not-in-catalog" },
          "bosses": { "40": "gohma", "378": "ganon" },
          "objectives": {
            "quest": { "18": "deku-tree", "0": "forest-temple" },
            "items": { "59": "kokiri-forest" },
            "bosses": { "ganon": "ganons-castle" }
          },
          "finishBoss": "ganon"
        }
        """;

    private sealed class Rig
    {
        public readonly List<ZeldathonOutbound> Sent = [];
        public readonly ZeldathonClock Clock = new(() => 0);
        public long Now;
        public int ClockRequests;
        public readonly ZeldathonSession Session;

        public Rig()
        {
            Session = new ZeldathonSession(Clock, Sent.Add, () => ClockRequests++, () => Now, ZeldathonMap.Parse(MapJson));
            // Igual que en ZeldathonService: cada reloj nuevo del servidor dispara la conciliación.
            Clock.Changed += Session.Reconcile;
        }

        public void Status(ZeldathonRacerStatus status)
        {
            Clock.Update(new ZeldathonClockSnapshot("ralbat", 3_600_000, status, DateTime.UtcNow, DateTime.UtcNow));
        }

        public void Game(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var name = doc.RootElement.GetProperty("event").GetString()!;
            Session.OnGameEvent(name, doc.RootElement.Clone());
        }

        public IEnumerable<string> Types => Sent.Select(m => m.Type);

        public List<JsonNode> Of(string type) =>
            Sent.Where(m => m.Type == type).Select(m => JsonNode.Parse(m.Json)!).ToList();

        public void Live()
        {
            Status(ZeldathonRacerStatus.Online);
            Game("""{"event":"game_session","state":"loaded"}""");
            Status(ZeldathonRacerStatus.Live);
        }
    }

    [Fact]
    public void The_map_keeps_valid_entries_and_drops_unknown_catalog_ids()
    {
        var map = ZeldathonMap.Parse(MapJson);
        Assert.Equal("kokiri-forest", map.Areas[85]);
        Assert.Equal("hookshot", map.Items[10]);
        Assert.False(map.Items.ContainsKey(999));
        Assert.Equal("forest-temple", map.QuestObjectives[0]);
        Assert.Equal("kokiri-forest", map.ItemObjectives[59]);
        Assert.Equal("ganons-castle", map.BossObjectives["ganon"]);
        Assert.Equal("ganon", map.FinishBoss);
    }

    [Fact]
    public void The_shipped_profile_map_is_valid_and_complete()
    {
        var dir = AppContext.BaseDirectory;
        string? found = null;
        for (var d = new DirectoryInfo(dir); d != null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "config", "profiles", "ocarina-of-time", ZeldathonMap.FileName);
            if (File.Exists(candidate))
            {
                found = candidate;
                break;
            }
        }

        Assert.NotNull(found);
        var map = ZeldathonMap.Load(found!);
        Assert.False(map.IsEmpty);
        // Cada objetivo del catálogo se puede completar de alguna forma.
        var reachable = map.QuestObjectives.Values.Concat(map.ItemObjectives.Values).Concat(map.BossObjectives.Values).ToHashSet();
        Assert.All(ZeldathonCatalog.Objectives, o => Assert.Contains(o, reachable));
        Assert.Equal(map.Items.Values.Distinct().Count(), map.Items.Count);
        Assert.All(ZeldathonCatalog.Items, i => Assert.Contains(i, map.Items.Values));
    }

    [Fact]
    public void A_profile_without_its_own_map_uses_the_factory_copy()
    {
        var empty = Path.Combine(Path.GetTempPath(), $"hs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(empty);
        try
        {
            Assert.False(ZeldathonMap.LoadForProfile(empty, "ocarina-of-time").IsEmpty);
            Assert.True(ZeldathonMap.LoadForProfile(empty, "otro-juego").IsEmpty);

            File.WriteAllText(Path.Combine(empty, ZeldathonMap.FileName), """{"areas":{"1":"solo-esta"}}""");
            var own = ZeldathonMap.LoadForProfile(empty, "ocarina-of-time");
            Assert.Equal("solo-esta", own.Areas[1]);
            Assert.Empty(own.Items);
        }
        finally
        {
            Directory.Delete(empty, true);
        }
    }

    [Fact]
    public void Starts_the_session_first_and_holds_progress_until_the_clock_is_live()
    {
        var rig = new Rig();
        rig.Status(ZeldathonRacerStatus.Online);
        rig.Game("""{"event":"game_session","state":"loaded"}""");
        rig.Game("""{"event":"scene","scene":85}""");
        rig.Game("""{"event":"item","item":10}""");

        Assert.Equal(["SESSION_STARTED"], rig.Types);
        Assert.Equal(1, rig.ClockRequests);

        rig.Status(ZeldathonRacerStatus.Live); // el servidor confirmó
        Assert.Contains("AREA_CHANGED", rig.Types);
        Assert.Contains("ITEM_ACQUIRED", rig.Types);
        Assert.Contains("GAME_PROGRESS", rig.Types);
        Assert.Equal("kokiri-forest", rig.Of("AREA_CHANGED").Single()["area"]!.GetValue<string>());
    }

    [Fact]
    public void Retries_the_start_only_every_ten_seconds()
    {
        var rig = new Rig();
        rig.Status(ZeldathonRacerStatus.Online);
        rig.Game("""{"event":"game_session","state":"loaded"}""");
        rig.Session.Reconcile();
        rig.Now += 5_000;
        rig.Session.Reconcile();
        Assert.Single(rig.Of("SESSION_STARTED"));
        rig.Now += 6_000;
        rig.Session.Reconcile();
        Assert.Equal(2, rig.Of("SESSION_STARTED").Count);
        Assert.Equal(2, rig.Sent.Select(m => m.Id).Distinct().Count());
    }

    [Fact]
    public void Sends_only_what_changed()
    {
        var rig = new Rig();
        rig.Live();
        rig.Game("""{"event":"scene","scene":85}""");
        rig.Game("""{"event":"scene","scene":85}""");
        rig.Game("""{"event":"scene","scene":999}""");
        rig.Game("""{"event":"item","item":10}""");
        rig.Game("""{"event":"item","item":10}""");
        rig.Game("""{"event":"item","item":12345}""");
        rig.Game("""{"event":"scene","scene":81}""");

        Assert.Equal(2, rig.Of("AREA_CHANGED").Count);
        var items = rig.Of("ITEM_ACQUIRED");
        Assert.Single(items);
        Assert.Equal("hookshot", items[0]["item"]!.GetValue<string>());
    }

    [Fact]
    public void Quest_bits_and_items_complete_objectives_in_catalog_order()
    {
        var rig = new Rig();
        rig.Live();
        rig.Game("""{"event":"item","item":59}""");                    // espada Kokiri -> kokiri-forest
        rig.Game("""{"event":"quest","items":262145}""");              // bits 0 y 18
        var progress = rig.Of("GAME_PROGRESS").Last()["progress"]!;
        Assert.Equal(30.0, progress["percentage"]!.GetValue<double>());
        Assert.Equal(["kokiri-forest", "deku-tree", "forest-temple"],
            progress["completedObjectives"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray());
        Assert.Equal("dodongos-cavern", progress["currentObjective"]!.GetValue<string>());
    }

    [Fact]
    public void Stats_are_partial_and_include_the_absolute_boss_count()
    {
        var rig = new Rig();
        rig.Live();
        rig.Game("""{"event":"stats","hearts":5.5,"maxHearts":6,"rupees":120,"skulltulas":3}""");
        rig.Game("""{"event":"boss_defeated","actor":40}""");

        var boss = rig.Of("BOSS_DEFEATED").Single();
        Assert.Equal("gohma", boss["boss"]!.GetValue<string>());
        var stats = rig.Of("STATS_UPDATED").Last()["stats"]!;
        Assert.Equal(1, stats["bossesDefeated"]!.GetValue<int>());
        Assert.Equal(5.5, stats["hearts"]!.GetValue<double>());
        Assert.Equal(120, stats["rupees"]!.GetValue<long>());
    }

    [Fact]
    public void Finishes_only_with_the_final_boss_and_every_objective()
    {
        var rig = new Rig();
        rig.Live();
        rig.Game("""{"event":"boss_defeated","actor":378}""");
        Assert.DoesNotContain("GAME_FINISHED", rig.Types); // faltan objetivos

        // Se completan los que faltan por medios de la tabla (aquí solo hay algunos: se fuerzan en el mapa).
        var map = rig.Session.Map;
        map.QuestObjectives.Clear();
        for (var i = 0; i < ZeldathonCatalog.Objectives.Count; i++)
        {
            map.QuestObjectives[i] = ZeldathonCatalog.Objectives[i];
        }

        rig.Game("""{"event":"quest","items":1023}""");
        Assert.Single(rig.Of("GAME_FINISHED"));
        rig.Game("""{"event":"quest","items":1023}""");
        rig.Session.Reconcile();
        Assert.Single(rig.Of("GAME_FINISHED"));
    }

    [Fact]
    public void A_reconnect_resends_progress_but_never_replays_boss_events()
    {
        var rig = new Rig();
        rig.Live();
        rig.Game("""{"event":"item","item":10}""");
        rig.Game("""{"event":"boss_defeated","actor":40}""");
        rig.Sent.Clear();

        var wanted = 0;
        rig.Session.SnapshotWanted += () => wanted++;
        rig.Session.OnConnected();
        rig.Status(ZeldathonRacerStatus.Live);

        Assert.Equal(1, wanted);
        Assert.Contains("ITEM_ACQUIRED", rig.Types);
        Assert.Contains("STATS_UPDATED", rig.Types);
        Assert.DoesNotContain("BOSS_DEFEATED", rig.Types);
    }

    [Fact]
    public void A_rejection_out_of_sequence_makes_everything_be_resent()
    {
        var rig = new Rig();
        rig.Live();
        rig.Game("""{"event":"item","item":10}""");
        rig.Sent.Clear();

        rig.Session.OnRejected("GAME_PROGRESS", "out_of_sequence");
        rig.Session.Reconcile();
        Assert.Contains("ITEM_ACQUIRED", rig.Types);
        Assert.Contains("GAME_PROGRESS", rig.Types);
    }

    [Fact]
    public void Closing_the_game_ends_the_session_and_a_new_save_starts_a_clean_run()
    {
        var rig = new Rig();
        rig.Live();
        rig.Game("""{"event":"item","item":10}""");
        rig.Game("""{"event":"game_session","state":"exited"}""");
        Assert.Single(rig.Of("SESSION_ENDED"));
        Assert.False(rig.Session.GameActive);

        rig.Status(ZeldathonRacerStatus.Online);
        rig.Game("""{"event":"game_session","state":"loaded"}""");
        rig.Status(ZeldathonRacerStatus.Live);
        Assert.Empty(rig.Session.CompletedObjectives);
        Assert.Single(rig.Of("ITEM_ACQUIRED")); // el del primer archivo, no se repite
    }

    [Fact]
    public void Nothing_is_sent_while_disconnected_or_exhausted_and_unknown_events_are_ignored()
    {
        var rig = new Rig();
        rig.Game("""{"event":"game_session","state":"loaded"}""");
        Assert.Empty(rig.Sent); // sin reloj todavía

        rig.Status(ZeldathonRacerStatus.Exhausted);
        rig.Game("""{"event":"scene","scene":85}""");
        Assert.Empty(rig.Sent);

        rig.Game("""{"event":"player_death","deaths":3}""");
        rig.Game("""{"event":"algo_raro"}""");
        Assert.Empty(rig.Sent);
    }
}
