using System.Collections.Concurrent;
using System.Text.Json;
using HiveShock.Configuration;
using HiveShock.Hosting;
using HiveShock.Live;
using HiveShock.Logging;
using HiveShock.Networking;
using HiveShock.Tests.Input;

namespace HiveShock.Tests.Live;

/// <summary>Chat, seguir, compartir, bits y metas con varios efectos, con el router de verdad y el juego en dry-run.</summary>
public sealed class MultiEffectRouterTests : IDisposable
{
    private const string GiftsJson = """
        {
          "gifts": [],
          "follow": { "effect": "a", "effects": [ "a", "b", { "effect": "c", "delaySec": 0.3 } ] },
          "share": { "effect": "a", "effects": [ "a", "b" ] },
          "chat": { "enabled": true, "prefix": "!", "cooldownSec": 0, "globalGapSec": 0, "commands": {
              "combo": { "effect": "a", "effects": [ "a", "b" ] }, "solo": "c" } },
          "twitch": { "bits": [ { "min": 1, "effect": "a", "effects": [ "a", "c" ] } ] }
        }
        """;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hiveshock-multi-" + Guid.NewGuid().ToString("N"));
    private readonly ConcurrentQueue<string> _log = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly ViewerGuard _guard = new(() => null, _ => { });
    private readonly FollowGate _followGate;
    private readonly EffectDispatcher _dispatcher;
    private readonly LiveEffectRouter _router;
    private readonly Task _pump;

    public MultiEffectRouterTests()
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
        var gifts = new GiftConfigStore(effects);
        gifts.Reload(giftsPath);

        var options = new BridgeOptions { DryRun = true, EffectGapMs = 0, TikTokUniqueId = "streamer", TwitchUserLogin = "streamer" };
        _dispatcher = new EffectDispatcher(effects, new GameTcpClient(options), _guard);
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

    private List<string> SentTo(string who) => _log
        .Where(m => m.StartsWith("OK ", StringComparison.Ordinal) && m.Contains(" <- " + who + " "))
        .Select(m => m.Split(' ')[1])
        .ToList();

    [Fact]
    public async Task A_follow_launches_all_its_effects_in_order_and_the_waiting_one_comes_last()
    {
        _router.HandleFollow(Tt("7001", "seguidor_multi"), _cts.Token);

        await Wait.UntilAsync(() => SentTo("seguidor_multi").Count == 3);

        Assert.Equal(["a", "b", "c"], SentTo("seguidor_multi"));
    }

    [Fact]
    public async Task Share_and_bits_launch_every_effect()
    {
        _router.HandleShare(Tt("7002", "compartidor_multi"), _cts.Token);
        _router.HandleCheer(Tw("7003", "bits_multi"), 50, _cts.Token);

        await Wait.UntilAsync(() => SentTo("compartidor_multi").Count == 2 && SentTo("bits_multi").Count == 2);

        Assert.Equal(["a", "b"], SentTo("compartidor_multi"));
        Assert.Equal(["a", "c"], SentTo("bits_multi"));
    }

    [Fact]
    public async Task A_chat_command_with_several_effects_runs_them_all_and_a_single_one_still_works()
    {
        _router.HandleChat(Tt("7004", "chat_multi"), "!combo", _cts.Token);
        _router.HandleChat(Tt("7005", "chat_solo"), "!solo", _cts.Token);

        await Wait.UntilAsync(() => SentTo("chat_multi").Count == 2 && SentTo("chat_solo").Count == 1);

        Assert.Equal(["a", "b"], SentTo("chat_multi"));
        Assert.Equal(["c"], SentTo("chat_solo"));
    }
}
