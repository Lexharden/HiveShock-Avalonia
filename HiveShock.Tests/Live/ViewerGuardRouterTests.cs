using System.Collections.Concurrent;
using System.Text.Json;
using HiveShock.Configuration;
using HiveShock.Hosting;
using HiveShock.Live;
using HiveShock.Logging;
using HiveShock.Networking;
using TikTokLive.Proto;

namespace HiveShock.Tests.Live;

/// <summary>
/// El router de verdad con el juego en "dry-run": lo que llegaría al juego aparece en el log como
/// <c>OK efecto &lt;- usuario (motivo)</c>. Cada prueba usa nombres propios porque el log es global.
/// </summary>
public sealed class ViewerGuardRouterTests : IDisposable
{
    private const string GiftsJson = """
        {
          "gifts": [ { "gift": "Heart Me", "id": "7934", "effect": "impulse" } ],
          "follow": { "effect": "impulse", "oncePerUser": true },
          "share": { "effect": "impulse" },
          "likes": { "every": 3, "effect": "impulse" },
          "chat": { "enabled": true, "prefix": "!", "cooldownSec": 0, "globalGapSec": 0, "commands": { "salto": "impulse" } },
          "twitch": {
            "follow": { "effect": "impulse" },
            "bits": [ { "min": 1, "effect": "impulse" } ]
          }
        }
        """;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hiveshock-guard-" + Guid.NewGuid().ToString("N"));
    private readonly ConcurrentQueue<string> _log = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly ViewerGuard _guard = new(() => null, _ => { });
    private readonly FollowGate _followGate;
    private readonly EffectDispatcher _dispatcher;
    private readonly LiveEffectRouter _router;
    private readonly Task _pump;

    public ViewerGuardRouterTests() : this(startPump: true)
    {
    }

    private ViewerGuardRouterTests(bool startPump)
    {
        Directory.CreateDirectory(_dir);
        var giftsPath = Path.Combine(_dir, "gifts.json");
        File.WriteAllText(giftsPath, GiftsJson);

        var impulse = JsonDocument.Parse("""{ "action": "impulse" }""").RootElement
            .EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
        var effects = new EffectCatalog(new Dictionary<string, Dictionary<string, JsonElement>> { ["impulse"] = impulse });
        var gifts = new GiftConfigStore(effects);
        gifts.Reload(giftsPath);

        var options = new BridgeOptions { DryRun = true, EffectGapMs = 0, TikTokUniqueId = "streamer", TwitchUserLogin = "streamer" };
        var game = new GameTcpClient(options);
        _dispatcher = new EffectDispatcher(effects, game, _guard);
        _followGate = new FollowGate(() => null, _ => { }, () => DateTime.UtcNow);
        _router = new LiveEffectRouter(
            options,
            effects,
            gifts,
            new GiftCatalogStore(Path.Combine(_dir, "catalog.json")),
            _dispatcher,
            new OverlayNotifier(),
            new GiftGoalBank(),
            _guard,
            followGate: _followGate);

        BridgeLog.Logged += OnLog;
        _pump = startPump ? _dispatcher.RunAsync(_cts.Token) : Task.CompletedTask;
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
        _followGate.Dispose();
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

    private static ViewerIdentity Tt(string id, string handle) => new(LivePortIds.TikTok, id, handle, handle);

    private static ViewerIdentity Tw(string id, string login) => new(LivePortIds.Twitch, id, login, login);

    private bool Sent(string who) => _log.Any(m => m.StartsWith("OK impulse <- " + who + " ", StringComparison.Ordinal));

    private bool Discarded(string who) => _log.Any(m => m.StartsWith("Descartado impulse <- " + who + " ", StringComparison.Ordinal));

    /// <summary>Espera a que el efecto de <paramref name="who"/> llegue al juego.</summary>
    private void WaitSent(string who)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!Sent(who) && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(10);
        }

        Assert.True(Sent(who), $"{who} debía llegar al juego");
    }

    /// <summary>
    /// Manda un efecto de control y espera a que llegue: como la cola es en orden, todo lo anterior ya salió
    /// (o ya se descartó), así que se puede afirmar que algo NO llegó.
    /// </summary>
    private void Flush(string control)
    {
        _router.HandleShare(Tt("9999" + control.Length, control), _cts.Token);
        WaitSent(control);
    }

    [Fact]
    public void A_blocked_follower_triggers_nothing_and_a_normal_one_does()
    {
        _guard.Block(Tt("501", "bloqueado_f1"));

        _router.HandleFollow(Tt("501", "bloqueado_f1"), _cts.Token);
        _router.HandleFollow(Tt("502", "normal_f1"), _cts.Token);
        WaitSent("normal_f1");
        Flush("control_f1");

        Assert.False(Sent("bloqueado_f1"));
    }

    [Fact]
    public void A_blocked_follower_is_not_recorded_so_unblocking_lets_them_trigger_once()
    {
        _guard.Block(Tt("511", "bloqueado_f2"));
        _router.HandleFollow(Tt("511", "bloqueado_f2"), _cts.Token);

        _guard.Unblock(_guard.Blocked.Single(b => b.UserId == "511").Id);
        _router.HandleFollow(Tt("511", "bloqueado_f2"), _cts.Token);
        _router.HandleFollow(Tt("511", "bloqueado_f2"), _cts.Token); // repetido: la puerta lo frena

        WaitSent("bloqueado_f2");
        Flush("control_f2");
        Assert.Equal(1, _log.Count(m => m.StartsWith("OK impulse <- bloqueado_f2 ", StringComparison.Ordinal)));
    }

    [Fact]
    public void Follow_without_a_stable_id_but_with_a_handle_is_still_limited_to_once()
    {
        _router.HandleFollow(new ViewerIdentity(LivePortIds.TikTok, null, "solo_handle", "solo_handle"), _cts.Token);
        _router.HandleFollow(new ViewerIdentity(LivePortIds.TikTok, null, "solo_handle", "solo_handle"), _cts.Token);

        WaitSent("solo_handle");
        Flush("control_f3");
        Assert.Equal(1, _log.Count(m => m.StartsWith("OK impulse <- solo_handle ", StringComparison.Ordinal)));
    }

    [Fact]
    public void Pause_stops_follow_share_chat_gift_like_and_bits_and_resume_restores_them()
    {
        _guard.SetPaused(true);

        _router.HandleFollow(Tt("601", "p_follow"), _cts.Token);
        _router.HandleShare(Tt("602", "p_share"), _cts.Token);
        _router.HandleChat(Tt("603", "p_chat"), "!salto", _cts.Token);
        _router.HandleCheer(Tw("604", "p_bits"), 100, _cts.Token);
        _router.HandleTikTokGift(new WebcastGiftMessage
        {
            GiftId = 7934,
            GiftDetails = new GiftDetails { GiftName = "Heart Me", DiamondCount = 1, Id = 7934 },
            User = new UserIdentity { UserId = 605, UniqueId = "p_gift", Nickname = "p_gift" },
        }, _cts.Token);
        for (var i = 0; i < 6; i++)
        {
            _router.HandleLike(Tt("606", "p_like"), 1, _cts.Token);
        }

        _guard.SetPaused(false);
        _router.HandleShare(Tt("610", "reanudado"), _cts.Token);
        WaitSent("reanudado");

        foreach (var who in new[] { "p_follow", "p_share", "p_chat", "p_bits", "p_gift", "p_like" })
        {
            Assert.False(Sent(who), who);
        }
    }

    [Fact]
    public void Likes_during_the_pause_do_not_pile_up_for_when_it_ends()
    {
        _guard.SetPaused(true);
        for (var i = 0; i < 30; i++)
        {
            _router.HandleLike(Tt("621", "l_pausa"), 1, _cts.Token);
        }

        _guard.SetPaused(false);
        _router.HandleLike(Tt("622", "l_tras"), 1, _cts.Token); // 1 like de 3: no dispara
        Flush("control_l1");

        Assert.False(Sent("l_pausa"));
        Assert.False(Sent("l_tras"));
    }

    [Fact]
    public void A_follow_during_the_pause_is_still_noted_so_it_cannot_retrigger_after_resuming()
    {
        _guard.SetPaused(true);
        _router.HandleFollow(Tt("631", "f_pausa"), _cts.Token);
        _guard.SetPaused(false);
        _router.HandleFollow(Tt("631", "f_pausa"), _cts.Token);
        Flush("control_f4");

        Assert.False(Sent("f_pausa"));
    }

    [Fact]
    public void Blocked_chat_commands_gifts_and_bits_do_nothing()
    {
        _guard.Block(Tt("641", "b_chat"));
        _guard.Block(Tt("642", "b_gift"));
        _guard.Block(Tw("643", "b_bits"));

        _router.HandleChat(Tt("641", "b_chat"), "!salto", _cts.Token);
        _router.HandleTikTokGift(new WebcastGiftMessage
        {
            GiftId = 7934,
            GiftDetails = new GiftDetails { GiftName = "Heart Me", DiamondCount = 1, Id = 7934 },
            User = new UserIdentity { UserId = 642, UniqueId = "b_gift", Nickname = "b_gift" },
        }, _cts.Token);
        _router.HandleCheer(Tw("643", "b_bits"), 50, _cts.Token);
        _router.HandleChat(Tt("644", "ok_chat"), "!salto", _cts.Token);
        WaitSent("ok_chat");
        Flush("control_b1");

        Assert.False(Sent("b_chat"));
        Assert.False(Sent("b_gift"));
        Assert.False(Sent("b_bits"));
    }

    [Fact]
    public void A_gift_from_someone_not_blocked_reaches_the_game_and_is_listed_for_moderation()
    {
        _router.HandleTikTokGift(new WebcastGiftMessage
        {
            GiftId = 7934,
            GiftDetails = new GiftDetails { GiftName = "Heart Me", DiamondCount = 1, Id = 7934 },
            User = new UserIdentity { UserId = 651, UniqueId = "g_ok", Nickname = "g_ok" },
        }, _cts.Token);

        WaitSent("g_ok");
        Assert.Contains(_guard.Recent, a => a.Viewer.UserId == "651" && a.Verdict == ViewerVerdict.Allowed);
    }

    [Fact]
    public async Task Pausing_discards_effects_that_were_already_waiting()
    {
        // Sin bomba: los efectos se quedan en cola hasta que arranque.
        using var held = new ViewerGuardRouterTests(startPump: false);
        held._router.HandleFollow(Tt("661", "en_cola_1"), CancellationToken.None);
        held._router.HandleShare(Tt("662", "en_cola_2"), CancellationToken.None);

        held._guard.SetPaused(true);
        held._guard.SetPaused(false);
        held._router.HandleShare(Tt("663", "despues"), CancellationToken.None);
        var pump = held._dispatcher.RunAsync(held._cts.Token);
        held.WaitSent("despues");

        Assert.False(held.Sent("en_cola_1"));
        Assert.False(held.Sent("en_cola_2"));
        Assert.True(held.Discarded("en_cola_1"));
        Assert.True(held.Discarded("en_cola_2"));
        await held._cts.CancelAsync();
        await pump;
    }
}
