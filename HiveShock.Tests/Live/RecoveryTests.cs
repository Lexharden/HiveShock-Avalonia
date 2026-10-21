using System.Text.Json;
using HiveShock.Configuration;
using HiveShock.Hosting;
using HiveShock.Live;
using HiveShock.Networking;
using TikTokLive.Connection;

namespace HiveShock.Tests.Live;

public sealed class RecoveryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hiveshock-recovery-" + Guid.NewGuid().ToString("N"));
    private DateTimeOffset _now = new(2026, 10, 21, 20, 0, 0, TimeSpan.Zero);

    public RecoveryTests() => Directory.CreateDirectory(_dir);

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

    private LiveEffectRouter Router()
    {
        var effects = new EffectCatalog(new Dictionary<string, Dictionary<string, JsonElement>>());
        var options = new BridgeOptions { DryRun = true, EffectGapMs = 0, TwitchUserLogin = "streamer" };
        var dispatcher = new EffectDispatcher(effects, new GameTcpClient(options));
        return new LiveEffectRouter(
            options, effects, new GiftConfigStore(effects), new GiftCatalogStore(Path.Combine(_dir, "c.json")),
            dispatcher, new OverlayNotifier(), new GiftGoalBank(), new ViewerGuard(() => null, _ => { }));
    }

    private static TwitchFollower Follower(string id, DateTimeOffset at) =>
        new(id, "user" + id, "User" + id, at.ToString("yyyy-MM-ddTHH:mm:ssZ"), at);

    [Fact]
    public void Resume_point_is_sent_only_for_the_same_room_and_while_fresh()
    {
        var point = new ResumePoint();
        Assert.False(point.TryGet("room1", TimeSpan.FromMinutes(10), out _, out _));

        point.Update("room1", "cursor-1", "ext|1");

        Assert.True(point.TryGet("room1", TimeSpan.FromMinutes(10), out var cursor, out var ext));
        Assert.Equal("cursor-1", cursor);
        Assert.Equal("ext|1", ext);
        Assert.False(point.TryGet("otra-sala", TimeSpan.FromMinutes(10), out _, out _));
        Assert.False(point.TryGet("room1", TimeSpan.Zero, out _, out _));

        point.Clear();
        Assert.False(point.TryGet("room1", TimeSpan.FromMinutes(10), out _, out _));
    }

    [Fact]
    public void Resume_point_ignores_an_empty_cursor()
    {
        var point = new ResumePoint();
        point.Update("room1", "bueno", "x");
        point.Update("room1", "", "y");

        Assert.True(point.TryGet("room1", TimeSpan.FromMinutes(10), out var cursor, out _));
        Assert.Equal("bueno", cursor);
    }

    [Fact]
    public void Replay_probe_flags_only_messages_from_before_the_reconnect_inside_the_window()
    {
        var probe = new ReplayProbe(() => _now);
        var longAgo = _now.AddSeconds(-30).ToUnixTimeMilliseconds();

        Assert.False(probe.IsReplay(longAgo, isGift: true), "aún no se conectó");

        probe.NoteConnected();
        _now += TimeSpan.FromSeconds(2);

        Assert.True(probe.IsReplay(longAgo, isGift: true));
        Assert.True(probe.IsReplay(longAgo, isGift: false));
        Assert.False(probe.IsReplay(_now.ToUnixTimeMilliseconds(), isGift: true), "uno en vivo no cuenta");
        Assert.Equal(1, probe.ReplayedGifts);
        Assert.Equal(1, probe.ReplayedChats);

        _now += TimeSpan.FromSeconds(30);
        Assert.False(probe.IsReplay(longAgo, isGift: true), "fuera de la ventana inicial");
    }

    [Fact]
    public void Helix_followers_response_is_parsed()
    {
        using var doc = JsonDocument.Parse(
            "{ \"data\": [" +
            "{ \"user_id\": \"1\", \"user_login\": \"ana\", \"user_name\": \"Ana\", \"followed_at\": \"2026-10-21T19:59:00Z\" }," +
            "{ \"user_id\": \"2\", \"user_login\": \"luis\", \"user_name\": \"Luis\", \"followed_at\": \"no es fecha\" }" +
            "] }");

        var list = TwitchApi.ParseFollowers(doc.RootElement);

        var only = Assert.Single(list);
        Assert.Equal("ana", only.Login);
        Assert.Equal(new DateTimeOffset(2026, 10, 21, 19, 59, 0, TimeSpan.Zero), only.FollowedAt);
    }

    [Fact]
    public void Recovery_processes_only_new_followers_since_the_cut_and_only_once()
    {
        using var router = Router();
        var since = _now.AddMinutes(-2);
        var followers = new[]
        {
            Follower("10", _now.AddMinutes(-1)),   // durante el corte
            Follower("11", _now.AddSeconds(-30)),  // durante el corte
            Follower("12", _now.AddHours(-3)),     // de antes: no es del corte
        };

        Assert.Equal(2, router.RecoverTwitchFollows(followers, since, CancellationToken.None));
        Assert.Equal(0, router.RecoverTwitchFollows(followers, since, CancellationToken.None));
    }

    [Fact]
    public void A_follower_already_seen_live_is_not_recovered_again()
    {
        using var router = Router();
        var live = Follower("20", _now.AddMinutes(-1));

        // Lo mismo que hace el cliente de EventSub con channel.follow: reclama la clave con followed_at.
        Assert.True(router.Accept("tw", TwitchEventSubClient.FollowKey(live.UserId, live.FollowedAtRaw), 0, TimeSpan.MaxValue, "Evento de Twitch"));

        Assert.Equal(0, router.RecoverTwitchFollows([live], _now.AddMinutes(-5), CancellationToken.None));
    }

    [Fact]
    public void Live_follow_notification_and_recovery_share_the_same_key()
    {
        var claimed = new List<string>();
        using var doc = JsonDocument.Parse(
            "{ \"metadata\": { \"message_id\": \"m1\", \"subscription_type\": \"channel.follow\" }," +
            " \"payload\": { \"event\": { \"user_id\": \"30\", \"user_login\": \"eva\", \"user_name\": \"Eva\", \"followed_at\": \"2026-10-21T19:58:00Z\" } } }");

        TwitchEventSubClient.HandleNotification(doc.RootElement, (_, _) => { }, _ => { }, (_, _) => { }, id =>
        {
            claimed.Add(id);
            return true;
        });

        Assert.Contains(TwitchEventSubClient.FollowKey("30", "2026-10-21T19:58:00Z"), claimed);
    }
}
