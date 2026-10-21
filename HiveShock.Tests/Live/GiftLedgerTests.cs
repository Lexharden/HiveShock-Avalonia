using System.Collections.Concurrent;
using System.Text.Json;
using HiveShock.Configuration;
using HiveShock.Hosting;
using HiveShock.Live;
using HiveShock.Logging;
using HiveShock.Networking;
using HiveShock.Tests.Input;

namespace HiveShock.Tests.Live;

public sealed class GiftLedgerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hiveshock-ledger-" + Guid.NewGuid().ToString("N"));
    private DateTimeOffset _now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    public GiftLedgerTests() => Directory.CreateDirectory(_dir);

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

    private string LedgerPath => Path.Combine(_dir, GiftLedger.FileName);

    private GiftLedger Ledger(string? path = null, TimeSpan? window = null, int max = 5000) =>
        new(path, window, max, () => _now);

    private LiveEffectRouter Router()
    {
        var effects = Catalog();
        var options = new BridgeOptions { DryRun = true, EffectGapMs = 0 };
        var dispatcher = new EffectDispatcher(effects, new GameTcpClient(options));
        return new LiveEffectRouter(
            options, effects, new GiftConfigStore(effects), new GiftCatalogStore(Path.Combine(_dir, "c.json")),
            dispatcher, new OverlayNotifier(), new GiftGoalBank(), new ViewerGuard(() => null, _ => { }));
    }

    private static EffectCatalog Catalog()
    {
        var impulse = JsonDocument.Parse("{ \"action\": \"impulse\" }").RootElement
            .EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
        return new EffectCatalog(new Dictionary<string, Dictionary<string, JsonElement>> { ["impulse"] = impulse });
    }

    [Fact]
    public void A_key_can_be_claimed_only_once()
    {
        using var ledger = Ledger();

        Assert.True(ledger.TryClaim("tt:gift:1"));
        Assert.False(ledger.TryClaim("tt:gift:1"));
        Assert.True(ledger.TryClaim("tt:gift:2"));
        Assert.Equal(1, ledger.Duplicates);
    }

    [Fact]
    public void A_key_can_be_claimed_again_after_the_window()
    {
        using var ledger = Ledger(window: TimeSpan.FromMinutes(10));
        Assert.True(ledger.TryClaim("k"));

        _now += TimeSpan.FromMinutes(11);

        Assert.True(ledger.TryClaim("k"));
    }

    [Fact]
    public void The_ledger_is_bounded()
    {
        using var ledger = Ledger(max: 50);
        for (var i = 0; i < 500; i++)
        {
            _now += TimeSpan.FromMilliseconds(1);
            ledger.TryClaim("k" + i);
        }

        Assert.False(ledger.TryClaim("k499"), "lo más reciente se conserva");
        Assert.True(ledger.TryClaim("k0"), "lo más antiguo se pudo desalojar");
    }

    [Fact]
    public void What_was_seen_survives_a_restart()
    {
        using (var first = Ledger(LedgerPath))
        {
            Assert.True(first.TryClaim("tt:gift:77"));
        }

        using var second = Ledger(LedgerPath);
        Assert.False(second.TryClaim("tt:gift:77"));
    }

    [Fact]
    public void A_damaged_last_line_does_not_break_loading()
    {
        using (var first = Ledger(LedgerPath))
        {
            first.TryClaim("buena");
        }

        File.AppendAllText(LedgerPath, "{\"op\":\"seen\",\"k\":\"cortada\",\"t\":");

        using var second = Ledger(LedgerPath);
        Assert.False(second.TryClaim("buena"));
        Assert.True(second.TryClaim("cortada"));
    }

    [Fact]
    public void Pending_effects_survive_a_restart_until_delivered()
    {
        using (var first = Ledger(LedgerPath))
        {
            var id = first.AddPending("impulse", "ana", "regalo", fromChat: false, new Dictionary<string, double> { ["value"] = 3 });
            first.AddPending("jump", "luis", "chat", fromChat: true, null);
            first.MarkDelivered(id);
        }

        using var second = Ledger(LedgerPath);
        var left = second.TakeReplayable(TimeSpan.FromMinutes(10));

        var only = Assert.Single(left);
        Assert.Equal("jump", only.EffectId);
        Assert.Equal("luis", only.User);
        Assert.True(only.FromChat);
    }

    [Fact]
    public void Expired_pending_effects_are_dropped_instead_of_replayed()
    {
        using var ledger = Ledger(LedgerPath);
        ledger.AddPending("impulse", "ana", "regalo", false, null);

        _now += TimeSpan.FromMinutes(30);

        Assert.Empty(ledger.TakeReplayable(TimeSpan.FromMinutes(10)));
        Assert.Equal(0, ledger.PendingCount);
    }

    [Fact]
    public void Discard_all_clears_the_pending_list_on_disk_too()
    {
        using (var first = Ledger(LedgerPath))
        {
            first.AddPending("impulse", "ana", "regalo", false, null);
            first.DiscardAllPending();
        }

        using var second = Ledger(LedgerPath);
        Assert.Empty(second.TakeReplayable(TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void Clock_skew_does_not_drop_live_gifts()
    {
        using var router = Router();

        // El reloj de este equipo va 1 hora adelantado: todos los mensajes llegan «con 1 h de retraso».
        var created = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds();
        Assert.True(router.Accept("tt:gift", "1", created, TimeSpan.FromMinutes(10), "Regalo"));
        Assert.True(router.Accept("tt:gift", "2", created + 5000, TimeSpan.FromMinutes(10), "Regalo"));
    }

    [Fact]
    public void Router_ignores_a_repeated_message_and_a_really_old_one()
    {
        using var router = Router();

        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Assert.True(router.Accept("tt:chat", "10", nowMs, LiveEffectRouter.ChatMaxAge, "Comentario"));
        Assert.False(router.Accept("tt:chat", "10", nowMs, LiveEffectRouter.ChatMaxAge, "Comentario"));
        // Un mensaje en vivo fija la referencia; el viejo queda muy por detrás.
        Assert.False(router.Accept("tt:chat", "11", nowMs - 300_000, LiveEffectRouter.ChatMaxAge, "Comentario"));
        Assert.True(router.Accept("tt:chat", "", 0, LiveEffectRouter.ChatMaxAge, "Sin id ni hora pasa"));
    }

    [Fact]
    public void Twitch_notification_with_a_repeated_message_id_is_handled_once()
    {
        var seen = new HashSet<string>();
        var follows = 0;
        using var doc = JsonDocument.Parse(
            "{ \"metadata\": { \"message_id\": \"abc-1\", \"subscription_type\": \"channel.follow\" }," +
            " \"payload\": { \"event\": { \"user_id\": \"1\", \"user_login\": \"ana\", \"user_name\": \"Ana\" } } }");

        for (var i = 0; i < 3; i++)
        {
            TwitchEventSubClient.HandleNotification(
                doc.RootElement, (_, _) => { }, _ => follows++, (_, _) => { }, id => seen.Add(id));
        }

        Assert.Equal(1, follows);
    }

    [Fact]
    public void Connection_gap_is_reported_only_after_a_real_cut()
    {
        var gap = new ConnectionGap(() => _now);

        Assert.Null(gap.NoteUp()); // primera conexión
        gap.NoteDown();
        _now += TimeSpan.FromSeconds(2);
        Assert.Null(gap.NoteUp()); // corte corto

        gap.NoteDown();
        _now += TimeSpan.FromSeconds(10);
        gap.NoteDown(); // un segundo aviso no reinicia el reloj
        _now += TimeSpan.FromSeconds(37);
        var cut = gap.NoteUp();

        Assert.Equal(TimeSpan.FromSeconds(47), cut);
        Assert.Contains("47 s", ConnectionGap.Describe("TikTok", cut!.Value));
    }

    [Fact]
    public async Task Dispatcher_replays_what_never_reached_the_game_and_marks_it_delivered()
    {
        var logged = new ConcurrentQueue<string>();
        void OnLog(LogEntry e) => logged.Enqueue(e.Message);
        BridgeLog.Logged += OnLog;
        try
        {
            using (var before = Ledger(LedgerPath))
            {
                before.AddPending("impulse", "rescatado_x", "regalo Rose", false, null);
                before.AddPending("reiniciar_pc", "nadie", "regalo", false, null);
            }

            using var ledger = Ledger(LedgerPath);
            var options = new BridgeOptions { DryRun = true, EffectGapMs = 0 };
            using var dispatcher = new EffectDispatcher(Catalog(), new GameTcpClient(options), ledger: ledger);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

            Assert.Equal(1, dispatcher.ReplayPending(TimeSpan.FromMinutes(10)));
            var pump = dispatcher.RunAsync(cts.Token);
            await Wait.UntilAsync(() => logged.Any(m => m.StartsWith("OK impulse <- rescatado_x ", StringComparison.Ordinal)));

            Assert.Equal(0, ledger.PendingCount);
            Assert.Equal(0, dispatcher.ReplayPending(TimeSpan.FromMinutes(10)));
            Assert.DoesNotContain(logged, m => m.StartsWith("OK reiniciar_pc", StringComparison.Ordinal));
            cts.Cancel();
            try
            {
                await pump;
            }
            catch (OperationCanceledException)
            {
            }
        }
        finally
        {
            BridgeLog.Logged -= OnLog;
        }
    }
}
