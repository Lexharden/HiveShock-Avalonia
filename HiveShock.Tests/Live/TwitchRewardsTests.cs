using System.Collections.Concurrent;
using System.Text.Json;
using HiveShock.Configuration;
using HiveShock.Hosting;
using HiveShock.Live;
using HiveShock.Logging;
using HiveShock.Networking;
using HiveShock.Tests.Input;

namespace HiveShock.Tests.Live;

/// <summary>Recompensas de puntos del canal de Twitch (fase 1: reaccionar a los canjes).</summary>
public sealed class TwitchRewardsTests : IDisposable
{
    private const string GiftsJson = """
        {
          "gifts": [],
          "twitch": {
            "rewards": [
              { "rewardId": "rw-salto", "title": "Saltar", "effect": "a", "effects": [ "a", "b" ] },
              { "rewardId": "", "title": "Soltar enemigo", "effect": "c" },
              { "rewardId": "rw-sin", "title": "Hidratarse", "effect": "" }
            ]
          }
        }
        """;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hiveshock-rewards-" + Guid.NewGuid().ToString("N"));
    private readonly ConcurrentQueue<string> _log = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly ViewerGuard _guard = new(() => null, _ => { });
    private readonly GiftConfigStore _gifts;
    private readonly EffectDispatcher _dispatcher;
    private readonly LiveEffectRouter _router;
    private readonly Task _pump;

    public TwitchRewardsTests()
    {
        Directory.CreateDirectory(_dir);
        var giftsPath = Path.Combine(_dir, "gifts.json");
        File.WriteAllText(giftsPath, GiftsJson);

        var map = new Dictionary<string, Dictionary<string, JsonElement>>();
        foreach (var id in new[] { "a", "b", "c" })
        {
            map[id] = JsonDocument.Parse($$"""{ "action": "{{id}}" }""").RootElement
                .EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
        }

        var effects = new EffectCatalog(map);
        _gifts = new GiftConfigStore(effects);
        _gifts.Reload(giftsPath);

        var options = new BridgeOptions { DryRun = true, EffectGapMs = 0, TwitchUserLogin = "streamer" };
        _dispatcher = new EffectDispatcher(effects, new GameTcpClient(options), _guard);
        _router = new LiveEffectRouter(
            options,
            effects,
            _gifts,
            new GiftCatalogStore(Path.Combine(_dir, "catalog.json")),
            _dispatcher,
            new OverlayNotifier(),
            new GiftGoalBank(),
            _guard,
            followGate: new FollowGate(() => null, _ => { }, () => DateTime.UtcNow));

        BridgeLog.Logged += OnLog;
        _pump = _dispatcher.RunAsync(_cts.Token);
    }

    public void Dispose()
    {
        BridgeLog.Logged -= OnLog;
        _cts.Cancel();
        try
        {
            _pump.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // la cancelación es lo esperado
        }

        _router.Dispose();
        _dispatcher.Dispose();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // el directorio temporal no importa
        }
    }

    private void OnLog(LogEntry entry) => _log.Enqueue(entry.Message);

    private static ViewerIdentity Tw(string id, string login) => new(LivePortIds.Twitch, id, login, login);

    private static TwitchRedemption Redeem(string rewardId, string title, string id = "") =>
        new(id.Length > 0 ? id : Guid.NewGuid().ToString("N"), rewardId, title, 500, "");

    private List<string> SentTo(string who) => _log
        .Where(m => m.StartsWith("OK ", StringComparison.Ordinal) && m.Contains(" <- " + who + " "))
        .Select(m => m.Split(' ')[1])
        .ToList();

    // ---------------------------------------------------------------- configuración

    [Fact]
    public void Reward_matches_by_id_even_if_renamed_and_falls_back_to_the_title()
    {
        var cfg = _gifts.Snapshot;

        Assert.Equal("a", cfg.MatchReward("rw-salto", "Otro nombre")?.Effect);
        Assert.Equal("c", cfg.MatchReward("id-que-no-existe", "soltar ENEMIGO")?.Effect);
        Assert.Null(cfg.MatchReward("id-que-no-existe", "Nada"));
        Assert.Null(cfg.MatchReward("", ""));
    }

    [Fact]
    public void A_reward_without_effect_is_not_a_rule()
    {
        Assert.Equal(2, _gifts.Snapshot.TwitchRewards.Count);
        Assert.Null(_gifts.Snapshot.MatchReward("rw-sin", "Hidratarse"));
    }

    [Fact]
    public void Editor_keeps_rewards_on_save_including_one_without_effect()
    {
        var path = Path.Combine(_dir, "gifts.json");
        var editor = new GiftFileEditor(path);
        editor.Load();
        Assert.Equal(3, editor.TwitchRewards.Count);
        Assert.Equal(["b"], editor.TwitchRewards[0].MoreSteps.Select(s => s.Effect));

        editor.TwitchRewards.Add(new EditableTwitchReward { RewardId = "", Title = "  ", Effect = "a" });
        editor.Save();

        var again = new GiftFileEditor(path);
        again.Load();
        Assert.Equal(3, again.TwitchRewards.Count); // la fila sin id ni nombre no se guarda
        Assert.Equal("rw-salto", again.TwitchRewards[0].RewardId);
        Assert.Equal("Saltar", again.TwitchRewards[0].Title);
        Assert.Equal("a", again.TwitchRewards[0].Effect);
        Assert.Equal(["b"], again.TwitchRewards[0].MoreSteps.Select(s => s.Effect));
        Assert.Equal("Hidratarse", again.TwitchRewards[2].Title);
        Assert.Equal("", again.TwitchRewards[2].Effect);
    }

    // ---------------------------------------------------------------- router

    [Fact]
    public async Task A_mapped_redemption_launches_all_its_effects()
    {
        _router.HandleTwitchRedemption(Tw("9001", "canjeador"), Redeem("rw-salto", "Saltar"), _cts.Token);
        _router.HandleTwitchRedemption(Tw("9002", "por_nombre"), Redeem("rw-nuevo", "Soltar enemigo"), _cts.Token);

        await Wait.UntilAsync(() => SentTo("canjeador").Count == 2 && SentTo("por_nombre").Count == 1);

        Assert.Equal(["a", "b"], SentTo("canjeador"));
        Assert.Equal(["c"], SentTo("por_nombre"));
    }

    [Fact]
    public void An_unmapped_reward_does_nothing_and_is_logged_once()
    {
        for (var i = 0; i < 3; i++)
        {
            _router.HandleTwitchRedemption(Tw("9003", "sediento"), Redeem("rw-sin", "Hidratarse"), _cts.Token);
        }

        Assert.Single(_log, m => m.Contains("«Hidratarse» canjeada sin efecto", StringComparison.Ordinal));
        Assert.DoesNotContain(_log, m => m.StartsWith("Recompensa sediento", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Paused_or_blocked_redemptions_do_not_reach_the_game()
    {
        _guard.Block(Tw("9004", "bloqueado_rw"));
        _router.HandleTwitchRedemption(Tw("9004", "bloqueado_rw"), Redeem("rw-salto", "Saltar"), _cts.Token);

        _guard.SetPaused(true);
        _router.HandleTwitchRedemption(Tw("9005", "en_pausa_rw"), Redeem("rw-salto", "Saltar"), _cts.Token);
        _guard.SetPaused(false);

        // Una que sí pasa, para saber que el despachador ya procesó lo anterior.
        _router.HandleTwitchRedemption(Tw("9006", "testigo_rw"), Redeem("rw-salto", "Saltar"), _cts.Token);
        await Wait.UntilAsync(() => SentTo("testigo_rw").Count == 2);

        Assert.Empty(SentTo("bloqueado_rw"));
        Assert.Empty(SentTo("en_pausa_rw"));
    }

    // ---------------------------------------------------------------- EventSub y Helix

    private static JsonDocument Notification(string messageId, string redemptionId) => JsonDocument.Parse($$"""
        {
          "metadata": { "message_id": "{{messageId}}", "subscription_type": "channel.channel_points_custom_reward_redemption.add" },
          "payload": { "event": {
            "id": "{{redemptionId}}", "user_id": "42", "user_login": "ana", "user_name": "Ana",
            "user_input": "hola", "status": "unfulfilled",
            "reward": { "id": "rw-salto", "title": "Saltar", "cost": 500, "prompt": "" }
          } }
        }
        """);

    [Fact]
    public void Redemption_notification_is_parsed_and_the_same_redemption_runs_once()
    {
        var seen = new HashSet<string>();
        var got = new List<(ViewerIdentity Viewer, TwitchRedemption Redemption)>();

        // Mismo canje dos veces con otro message_id (Twitch puede reenviarlo): sale una vez.
        foreach (var messageId in new[] { "m-1", "m-2", "m-1" })
        {
            using var doc = Notification(messageId, "canje-1");
            TwitchEventSubClient.HandleNotification(
                doc.RootElement, (_, _) => { }, _ => { }, (_, _) => { }, id => seen.Add(id), (v, r) => got.Add((v, r)));
        }

        var (viewer, redemption) = Assert.Single(got);
        Assert.Equal("42", viewer.UserId);
        Assert.Equal("ana", viewer.Handle);
        Assert.Equal(new TwitchRedemption("canje-1", "rw-salto", "Saltar", 500, "hola"), redemption);
    }

    [Fact]
    public void Redemption_notification_without_handler_is_ignored()
    {
        using var doc = Notification("m-3", "canje-2");
        TwitchEventSubClient.HandleNotification(doc.RootElement, (_, _) => { }, _ => { }, (_, _) => { });
    }

    [Fact]
    public void Channel_rewards_are_read_sorted_by_cost_with_their_state()
    {
        using var doc = JsonDocument.Parse("""
            { "data": [
              { "id": "r2", "title": "Caro", "cost": 5000, "is_enabled": true, "is_paused": true },
              { "id": "r1", "title": "Barato", "cost": 100, "is_enabled": true, "is_paused": false },
              { "id": "", "title": "Sin id", "cost": 1 },
              { "id": "r3", "title": "Apagado", "cost": 100, "is_enabled": false }
            ] }
            """);

        var rewards = TwitchRewardsApi.ParseRewards(doc.RootElement);

        Assert.Equal(["Apagado", "Barato", "Caro"], rewards.Select(r => r.Title));
        Assert.False(rewards[0].IsEnabled);
        Assert.True(rewards[2].IsPaused);
    }

    [Fact]
    public void Old_sessions_without_the_redemptions_scope_are_detected()
    {
        using var old = JsonDocument.Parse("""{ "scopes": [ "user:read:chat", "bits:read" ] }""");
        using var fresh = JsonDocument.Parse("""{ "scopes": [ "bits:read", "channel:manage:redemptions" ] }""");

        Assert.False(TwitchRewardsApi.HasRedemptionScope(TwitchRewardsApi.ParseScopes(old.RootElement)));
        Assert.True(TwitchRewardsApi.HasRedemptionScope(TwitchRewardsApi.ParseScopes(fresh.RootElement)));
        Assert.True(TwitchRewardsApi.HasRedemptionScope(["channel:read:redemptions"]));
        Assert.Contains(TwitchRewardsApi.ManageScope, TwitchAuthClient.DefaultScopes, StringComparison.Ordinal);
    }
}
