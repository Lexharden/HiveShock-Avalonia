using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using HiveShock.Configuration;
using HiveShock.Hosting;
using HiveShock.Live;
using HiveShock.Logging;
using HiveShock.Networking;
using HiveShock.Tests.Input;

namespace HiveShock.Tests.Live;

/// <summary>Fase 2: recompensas creadas por HiveShock (completar o devolver canjes, pausarlas, recuperar pendientes).</summary>
public sealed class TwitchRewardManagerTests : IDisposable
{
    private const string GiftsJson = """
        {
          "gifts": [],
          "twitch": {
            "rewards": [
              { "rewardId": "rw-mia", "title": "Saltar", "effect": "a", "effects": [ "a", "b" ] },
              { "rewardId": "rw-panel", "title": "Del panel", "effect": "a" },
              { "rewardId": "rw-mia-sin", "title": "Sin efecto", "effect": "" },
              { "rewardId": "rw-mia-rota", "title": "Rota", "effect": "no_existe" }
            ]
          }
        }
        """;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hiveshock-rwm-" + Guid.NewGuid().ToString("N"));
    private readonly ConcurrentQueue<string> _log = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly ViewerGuard _guard = new(() => null, _ => { });
    private readonly FakeSink _sink = new("rw-mia", "rw-mia-sin", "rw-mia-rota");
    private readonly GiftLedger _ledger = new();
    private readonly EffectDispatcher _dispatcher;
    private readonly LiveEffectRouter _router;
    private readonly Task _pump;

    public TwitchRewardManagerTests()
    {
        Directory.CreateDirectory(_dir);
        var giftsPath = Path.Combine(_dir, "gifts.json");
        File.WriteAllText(giftsPath, GiftsJson);

        var effects = Catalog("a", "b");
        var gifts = new GiftConfigStore(effects);
        gifts.Reload(giftsPath);

        var options = new BridgeOptions { DryRun = true, EffectGapMs = 0 };
        _dispatcher = new EffectDispatcher(effects, new GameTcpClient(options), _guard);
        _router = new LiveEffectRouter(
            options,
            effects,
            gifts,
            new GiftCatalogStore(Path.Combine(_dir, "catalog.json")),
            _dispatcher,
            new OverlayNotifier(),
            new GiftGoalBank(),
            _guard,
            followGate: new FollowGate(() => null, _ => { }, () => DateTime.UtcNow),
            ledger: _ledger,
            redemptions: _sink);

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
        _ledger.Dispose();
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

    private static EffectCatalog Catalog(params string[] ids) =>
        new(ids.ToDictionary(
            id => id,
            id => JsonDocument.Parse($$"""{ "action": "{{id}}" }""").RootElement
                .EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone())));

    private static ViewerIdentity Tw(string id, string login) => new(LivePortIds.Twitch, id, login, login);

    private static TwitchRedemption Redeem(string rewardId, string title = "Saltar", string? id = null) =>
        new(id ?? Guid.NewGuid().ToString("N"), rewardId, title, 500, "");

    // ---------------------------------------------------------------- PlanTracker

    [Fact]
    public void Tracker_finishes_once_and_knows_if_anything_arrived()
    {
        var results = new List<bool>();
        var tracker = new PlanTracker(3, results.Add);

        tracker.Report(false);
        tracker.Report(true);
        Assert.Empty(results);
        tracker.ReportDiscarded(1);
        tracker.Report(true); // de más: no vuelve a avisar

        Assert.Equal([true], results);
        Assert.True(tracker.IsFinished);

        var none = new List<bool>();
        new PlanTracker(2, none.Add).ReportDiscarded(2);
        Assert.Equal([false], none);
    }

    [Fact]
    public async Task Dispatcher_reports_delivered_and_unknown_effects_to_the_tracker()
    {
        var done = new TaskCompletionSource<bool>();
        var tracker = new PlanTracker(2, d => done.TrySetResult(d));
        _dispatcher.EnqueueSequence(
            [new PlannedEffect(new EffectStep("a"), "t"), new PlannedEffect(new EffectStep("no_existe"), "t")],
            "ana", _cts.Token, tracker: tracker);

        Assert.True(await done.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, tracker.Delivered);
    }

    [Fact]
    public async Task Pausing_discards_waiting_chained_effects_and_the_tracker_knows_nothing_arrived()
    {
        var done = new TaskCompletionSource<bool>();
        var tracker = new PlanTracker(1, d => done.TrySetResult(d));
        _dispatcher.EnqueueSequence([new PlannedEffect(new EffectStep("a", DelayMs: 300), "t")], "ana", _cts.Token, tracker: tracker);

        _guard.SetPaused(true);
        try
        {
            Assert.False(await done.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            _guard.SetPaused(false);
        }
    }

    // ---------------------------------------------------------------- router

    [Fact]
    public async Task A_managed_redemption_is_fulfilled_after_its_effects_reach_the_game()
    {
        var r = Redeem("rw-mia");
        _router.HandleTwitchRedemption(Tw("1", "ana"), r, _cts.Token);

        await Wait.UntilAsync(() => _sink.Resolved.ContainsKey(r.Id));
        Assert.Equal((true, ""), _sink.Resolved[r.Id]);
        Assert.Equal(2, _log.Count(m => m.StartsWith("OK ", StringComparison.Ordinal) && m.Contains(" <- ana ")));
    }

    [Fact]
    public void Managed_redemptions_that_cannot_run_give_the_points_back_at_once()
    {
        _guard.Block(Tw("2", "bloqueado"));
        var blocked = Redeem("rw-mia");
        _router.HandleTwitchRedemption(Tw("2", "bloqueado"), blocked, _cts.Token);

        var unmapped = Redeem("rw-mia-sin", "Sin efecto");
        _router.HandleTwitchRedemption(Tw("3", "ana"), unmapped, _cts.Token);

        _guard.SetPaused(true);
        var paused = Redeem("rw-mia");
        _router.HandleTwitchRedemption(Tw("4", "ana"), paused, _cts.Token);
        _guard.SetPaused(false);

        Assert.Equal((false, "espectador bloqueado"), _sink.Resolved[blocked.Id]);
        Assert.Equal((false, "no tiene efecto en este juego"), _sink.Resolved[unmapped.Id]);
        Assert.Equal((false, "efectos en pausa"), _sink.Resolved[paused.Id]);
    }

    [Fact]
    public async Task A_managed_redemption_whose_effect_never_reaches_the_game_gives_the_points_back()
    {
        var r = Redeem("rw-mia-rota", "Rota");
        _router.HandleTwitchRedemption(Tw("5", "ana"), r, _cts.Token);

        await Wait.UntilAsync(() => _sink.Resolved.ContainsKey(r.Id));
        Assert.Equal((false, "el efecto no llegó al juego"), _sink.Resolved[r.Id]);
    }

    [Fact]
    public void Redemptions_of_panel_rewards_are_never_resolved()
    {
        _guard.SetPaused(true);
        _router.HandleTwitchRedemption(Tw("6", "ana"), Redeem("rw-panel", "Del panel"), _cts.Token);
        _guard.SetPaused(false);

        Assert.Empty(_sink.Resolved);
    }

    [Fact]
    public async Task Recovery_completes_handled_ones_refunds_old_ones_and_runs_new_ones()
    {
        var now = DateTimeOffset.UtcNow;
        TwitchPendingRedemption Pending(string id, DateTimeOffset at) => new("9", "eva", "Eva", Redeem("rw-mia", id: id), at);

        _ledger.TryClaim("tw:" + TwitchEventSubClient.RedemptionKey("ya-visto"));
        _sink.PendingIds.Add("en-marcha");

        var launched = _router.RecoverTwitchRedemptions(
        [
            Pending("ya-visto", now.AddMinutes(-1)),
            Pending("viejo", now.AddHours(-3)),
            Pending("en-marcha", now.AddMinutes(-1)),
            Pending("nuevo", now.AddSeconds(-20)),
        ], _cts.Token);

        Assert.Equal(1, launched);
        Assert.Equal((true, ""), _sink.Resolved["ya-visto"]);
        Assert.False(_sink.Resolved["viejo"].Fulfilled);
        Assert.False(_sink.Resolved.ContainsKey("en-marcha"));
        await Wait.UntilAsync(() => _sink.Resolved.ContainsKey("nuevo"));
        Assert.Equal((true, ""), _sink.Resolved["nuevo"]);
    }

    // ---------------------------------------------------------------- gestor (Helix simulado)

    private static (TwitchRewardManager Manager, FakeHelix Helix) Manager()
    {
        var helix = new FakeHelix();
        var options = new BridgeOptions
        {
            TwitchClientId = "cid",
            TwitchAccessToken = "tok",
            TwitchUserId = "b1",
        };
        return (new TwitchRewardManager(options, new HttpClient(helix)), helix);
    }

    private const string MyRewards = """
        { "data": [
          { "id": "r-activa", "title": "Saltar", "cost": 100, "is_enabled": true, "is_paused": false },
          { "id": "r-pausada", "title": "Enemigo", "cost": 500, "is_enabled": true, "is_paused": true }
        ] }
        """;

    [Fact]
    public async Task Sync_only_patches_rewards_whose_pause_state_must_change()
    {
        var (manager, helix) = Manager();
        await using var _ = manager;
        helix.Respond = req => req.Method == HttpMethod.Get ? (HttpStatusCode.OK, MyRewards) : (HttpStatusCode.OK, "{}");

        Assert.True(await manager.RefreshManageableAsync(CancellationToken.None));
        Assert.True(manager.IsManaged("r-activa"));

        // En directo con efecto solo «r-pausada»: se reactiva esa y se pausa la otra.
        manager.RequestSync(["r-pausada"], live: true);
        await manager.FlushAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(
            ["id=r-activa {\"is_paused\":true}", "id=r-pausada {\"is_paused\":false}"],
            helix.Patches.Select(p => $"{p.Query} {p.Body}").Order());

        // Ya están como tocan: no se vuelve a pedir nada.
        helix.Patches.Clear();
        manager.RequestSync(["r-pausada"], live: true);
        await manager.FlushAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(helix.Patches);

        // Al desconectar, todas pausadas.
        manager.RequestSync(["r-pausada"], live: false);
        await manager.FlushAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(["id=r-pausada {\"is_paused\":true}"], helix.Patches.Select(p => $"{p.Query} {p.Body}"));
    }

    [Fact]
    public async Task Refund_cancels_the_redemption_and_disconnecting_settles_unfinished_plans()
    {
        var (manager, helix) = Manager();
        await using var _ = manager;
        helix.Respond = _ => (HttpStatusCode.OK, "{}");

        manager.Resolve(Redeem("r-activa", id: "c1"), "ana", fulfilled: false, "efectos en pausa");

        // Plan a medias al desconectar: sin nada entregado se devuelven los puntos; con algo, se completa.
        var pendingNothing = manager.Begin(Redeem("r-activa", id: "c2"), "ana", 2);
        var pendingSome = manager.Begin(Redeem("r-activa", id: "c3"), "ana", 2);
        pendingSome.Report(true);
        Assert.True(manager.IsPending("c2"));
        manager.SettleOutstanding(keepPending: false);
        pendingNothing.Report(true); // tarde: ya se resolvió, no se manda otra vez
        pendingNothing.Report(true);

        await manager.FlushAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(
            ["id=c1 {\"status\":\"CANCELED\"}", "id=c2 {\"status\":\"CANCELED\"}", "id=c3 {\"status\":\"FULFILLED\"}"],
            helix.Patches.Where(p => p.Path.EndsWith("/redemptions", StringComparison.Ordinal))
                .Select(p => $"{p.Query} {p.Body}").Order());
        Assert.Contains(_log, m => m.StartsWith("Puntos devueltos a ana por «Saltar» (efectos en pausa)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Closing_the_app_leaves_unfinished_redemptions_pending_in_twitch()
    {
        var (manager, helix) = Manager();
        await using var _ = manager;
        helix.Respond = _ => (HttpStatusCode.OK, "{}");

        manager.Begin(Redeem("r-activa", id: "c4"), "ana", 1);
        manager.SettleOutstanding(keepPending: true);
        await manager.FlushAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(helix.Patches);
        Assert.False(manager.IsPending("c4"));
    }

    [Fact]
    public async Task Creating_a_reward_never_lets_it_skip_the_queue_and_twitch_errors_are_shown()
    {
        var (manager, helix) = Manager();
        await using var _ = manager;
        helix.Respond = req => req.Method == HttpMethod.Post
            ? (HttpStatusCode.OK, """{ "data": [ { "id": "nueva", "title": "Saltar", "cost": 300, "is_enabled": true, "is_paused": false } ] }""")
            : (HttpStatusCode.BadRequest, """{ "message": "CREATE_CUSTOM_REWARD_DUPLICATE_REWARD" }""");

        var created = await manager.CreateAsync(new TwitchRewardSpec("Saltar", 300, GlobalCooldownSeconds: 30), CancellationToken.None);
        Assert.True(created.IsManageable);
        Assert.True(manager.IsManaged("nueva"));
        using (var body = JsonDocument.Parse(helix.Posts.Single()))
        {
            Assert.False(body.RootElement.GetProperty("should_redemptions_skip_request_queue").GetBoolean());
            Assert.True(body.RootElement.GetProperty("is_global_cooldown_enabled").GetBoolean());
            Assert.False(body.RootElement.GetProperty("is_max_per_stream_enabled").GetBoolean());
        }

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.UpdateAsync("nueva", new TwitchRewardSpec("Saltar", 300), CancellationToken.None));
        Assert.Equal("CREATE_CUSTOM_REWARD_DUPLICATE_REWARD", error.Message);

        await Assert.ThrowsAsync<ArgumentException>(
            () => manager.CreateAsync(new TwitchRewardSpec("", 0), CancellationToken.None));
    }

    [Fact]
    public void Reward_settings_and_pending_redemptions_are_read_from_helix()
    {
        using var rewards = JsonDocument.Parse("""
            { "data": [ { "id": "r1", "title": "Saltar", "cost": 100, "prompt": "¡Salta!",
              "global_cooldown_setting": { "is_enabled": true, "global_cooldown_seconds": 60 },
              "max_per_stream_setting": { "is_enabled": false, "max_per_stream": 5 },
              "max_per_user_per_stream_setting": { "is_enabled": true, "max_per_user_per_stream": 2 } } ] }
            """);
        var reward = Assert.Single(TwitchRewardsApi.ParseRewards(rewards.RootElement, new HashSet<string> { "r1" }));
        Assert.True(reward.IsManageable);
        Assert.Equal(new TwitchRewardSpec("Saltar", 100, "¡Salta!", 60, 0, 2), reward.ToSpec());

        using var pending = JsonDocument.Parse("""
            { "data": [ { "id": "c1", "user_id": "7", "user_login": "eva", "user_name": "Eva", "user_input": "",
              "status": "UNFULFILLED", "redeemed_at": "2026-11-19T10:00:00Z",
              "reward": { "id": "r1", "title": "Saltar", "cost": 100 } } ] }
            """);
        var p = Assert.Single(TwitchRewardsApi.ParsePendingRedemptions(pending.RootElement));
        Assert.Equal(("7", "eva", "c1", "r1"), (p.UserId, p.Login, p.Redemption.Id, p.Redemption.RewardId));
        Assert.Equal(new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero), p.RedeemedAt);
    }

    // ---------------------------------------------------------------- dobles

    private sealed class FakeSink(params string[] managed) : ITwitchRedemptionSink
    {
        private readonly HashSet<string> _managed = new(managed, StringComparer.OrdinalIgnoreCase);

        public ConcurrentDictionary<string, (bool Fulfilled, string Reason)> Resolved { get; } = new();
        public HashSet<string> PendingIds { get; } = [];

        public bool IsManaged(string rewardId) => _managed.Contains(rewardId);

        public PlanTracker Begin(TwitchRedemption redemption, string user, int steps) =>
            new(steps, d => Resolve(redemption, user, d, d ? "" : "el efecto no llegó al juego"));

        public void Resolve(TwitchRedemption redemption, string user, bool fulfilled, string reason) =>
            Resolved[redemption.Id] = (fulfilled, reason);

        public bool IsPending(string redemptionId) => PendingIds.Contains(redemptionId);
    }

    private sealed class FakeHelix : HttpMessageHandler
    {
        public Func<HttpRequestMessage, (HttpStatusCode, string)> Respond { get; set; } = _ => (HttpStatusCode.OK, "{}");
        public ConcurrentQueue<string> Posts { get; } = new();
        public List<(string Path, string Query, string Body)> Patches { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            if (request.Method == HttpMethod.Post)
            {
                Posts.Enqueue(body);
            }
            else if (request.Method.Method == "PATCH")
            {
                // Solo lo que identifica la petición: «id=…» (recompensa o canje).
                var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
                lock (Patches)
                {
                    Patches.Add((request.RequestUri.AbsolutePath, $"id={query["id"]}", body));
                }
            }

            var (status, json) = Respond(request);
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
