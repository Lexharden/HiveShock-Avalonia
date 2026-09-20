using System.Collections.Concurrent;
using HiveShock.Configuration;
using HiveShock.Hosting;
using HiveShock.Logging;
using HiveShock.Networking;
using TikTokLive.Helpers;
using TikTokLive.Proto;

namespace HiveShock.Live;

/// <summary>Traduce eventos de cualquier port a efectos del juego. Copy de logs estable para la UI.</summary>
public sealed class LiveEffectRouter : IDisposable
{
    private readonly BridgeOptions _options;
    private readonly EffectCatalog _effects;
    private readonly GiftConfigStore _gifts;
    private readonly GiftCatalogStore _catalog;
    private readonly EffectDispatcher _dispatcher;
    private readonly OverlayNotifier _overlay;
    private readonly GiftGoalBank _goals;
    private readonly GiftStreakTracker _streaks = new();
    private readonly object _streakGate = new();
    private readonly Timer _streakFlushTimer;
    private readonly ChatCommandGate _chatGate = new();
    private readonly ConcurrentDictionary<string, byte> _seenUnmapped = new();
    private long _likeBucket;

    public LiveEffectRouter(
        BridgeOptions options,
        EffectCatalog effects,
        GiftConfigStore gifts,
        GiftCatalogStore catalog,
        EffectDispatcher dispatcher,
        OverlayNotifier overlay,
        GiftGoalBank goals)
    {
        _options = options;
        _effects = effects;
        _gifts = gifts;
        _catalog = catalog;
        _dispatcher = dispatcher;
        _overlay = overlay;
        _goals = goals;
        _streakFlushTimer = new Timer(
            _ => FlushStaleStreaks(),
            null,
            GiftStreakTracker.PremiumComboIdleTimeout,
            TimeSpan.FromMilliseconds(150));
    }

    public void Dispose() => _streakFlushTimer.Dispose();

    public void HandleChat(string user, string? stableId, string text, CancellationToken ct, string portId)
    {
        if (_options.CaptureOnly)
        {
            return;
        }

        var cfg = string.Equals(portId, LivePortIds.Twitch, StringComparison.OrdinalIgnoreCase)
            ? _gifts.Snapshot.TwitchChat
            : _gifts.Snapshot.Chat;
        if (!cfg.Enabled)
        {
            return;
        }

        var prefix = cfg.Prefix ?? "!";
        var body = (text ?? "").Trim();
        if (body.Length == 0)
        {
            return;
        }

        if (!string.IsNullOrEmpty(prefix) && !body.StartsWith(prefix, StringComparison.Ordinal))
        {
            return;
        }

        if (!string.IsNullOrEmpty(prefix))
        {
            body = body[prefix.Length..];
        }

        var command = body.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault()
            ?.ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(command) || !cfg.Commands.TryGetValue(command, out var effectId))
        {
            return;
        }

        var viewer = Viewer(user);
        var key = ChatCommandGate.ViewerKey(portId, stableId);
        var admit = _chatGate.TryAdmit(key, cfg.CooldownSec, cfg.GlobalGapSec, _dispatcher.PendingChat);
        if (!admit.Allowed)
        {
            LogChatDenied(admit, viewer, prefix, command);
            return;
        }

        BridgeLog.Info($"Chat {viewer} {prefix}{command} -> {effectId}");
        _ = _dispatcher.EnqueueAsync(effectId, viewer, $"Chat {prefix}{command}", ct, fromChat: true);
    }

    public static string TikTokStableId(UserIdentity? user)
    {
        if (user?.UniqueId is { Length: > 0 } uid)
        {
            return uid;
        }

        if (user?.DisplayId is { Length: > 0 } display)
        {
            return display;
        }

        if (user is { UserId: > 0 })
        {
            return user.UserId.ToString();
        }

        if (user?.Nickname is { Length: > 0 } nick)
        {
            return nick;
        }

        return "anon";
    }

    private static void LogChatDenied(ChatAdmitResult admit, string viewer, string prefix, string command)
    {
        if (!admit.Log)
        {
            return;
        }

        if (admit.Kind == ChatAdmitKind.Cooldown)
        {
            var who = string.IsNullOrEmpty(viewer) ? "alguien" : viewer;
            BridgeLog.Info($"Chat {who} {prefix}{command} — espera {admit.RetrySec}s");
            return;
        }

        BridgeLog.Info("Chat saturado, se omiten comandos");
    }

    public void HandleFollow(string user, CancellationToken ct, string portId)
    {
        if (_options.CaptureOnly)
        {
            return;
        }

        var effect = string.Equals(portId, LivePortIds.Twitch, StringComparison.OrdinalIgnoreCase)
            ? _gifts.Snapshot.TwitchFollow.Effect
            : _gifts.Snapshot.Follow.Effect;
        if (string.IsNullOrWhiteSpace(effect))
        {
            return;
        }

        BridgeLog.Info($"Follow {Viewer(user)} -> {effect}");
        _ = _dispatcher.EnqueueAsync(effect, Viewer(user), "Follow", ct);
    }

    public void HandleCheer(string user, int bits, CancellationToken ct)
    {
        if (_options.CaptureOnly || bits <= 0)
        {
            return;
        }

        var match = _gifts.Snapshot.TwitchBits
            .Where(b => bits >= b.Min && !string.IsNullOrWhiteSpace(b.Effect))
            .MaxBy(b => b.Min);
        if (match == null)
        {
            return;
        }

        var viewer = Viewer(user);
        if (string.IsNullOrEmpty(viewer))
        {
            viewer = "Anónimo";
        }

        BridgeLog.Info($"Bits {viewer} x{bits} -> {match.Effect}");
        _ = _dispatcher.EnqueueAsync(match.Effect, viewer, $"Bits x{bits}", ct);
    }

    public void HandleShare(string user, CancellationToken ct)
    {
        if (_options.CaptureOnly)
        {
            return;
        }

        var effect = _gifts.Snapshot.Share.Effect;
        if (string.IsNullOrWhiteSpace(effect))
        {
            return;
        }

        BridgeLog.Info($"Share {Viewer(user)} -> {effect}");
        _ = _dispatcher.EnqueueAsync(effect, Viewer(user), "Share", ct);
    }

    public void HandleLike(string user, int likeCount, CancellationToken ct)
    {
        if (_options.CaptureOnly)
        {
            return;
        }

        var cfg = _gifts.Snapshot.Likes;
        if (cfg.Every <= 0 || string.IsNullOrWhiteSpace(cfg.Effect))
        {
            return;
        }

        var added = likeCount > 0 ? likeCount : 1;
        Interlocked.Add(ref _likeBucket, added);
        while (true)
        {
            var current = Interlocked.Read(ref _likeBucket);
            if (current < cfg.Every)
            {
                break;
            }

            if (Interlocked.CompareExchange(ref _likeBucket, current - cfg.Every, current) == current)
            {
                BridgeLog.Info($"Likes {Viewer(user)} x{cfg.Every} -> {cfg.Effect}");
                _ = _dispatcher.EnqueueAsync(cfg.Effect, Viewer(user), $"{cfg.Every} likes", ct);
            }
        }
    }

    public void HandleTikTokGift(WebcastGiftMessage gift, CancellationToken ct)
    {
        var name = gift.GiftDetails?.GiftName ?? "";
        var id = gift.GiftId != 0
            ? gift.GiftId.ToString()
            : gift.GiftDetails?.Id is > 0 ? gift.GiftDetails.Id.ToString() : "";
        var diamondsPer = gift.GiftDetails?.DiamondCount is > 0
            ? gift.GiftDetails.DiamondCount
            : (int?)null;

        if (_catalog.Observe(id, name, diamondsPer))
        {
            BridgeLog.Info($"Catálogo + {(!string.IsNullOrWhiteSpace(name) ? name : id)} id={id} ({diamondsPer ?? 0}d) total={_catalog.Count}");
        }

        GiftStreakEvent streak;
        lock (_streakGate)
        {
            streak = _streaks.Process(gift);
        }

        if (!streak.IsFinal)
        {
            return;
        }

        var user = ViewerName(gift.User);
        var repeat = Math.Max(1, streak.TotalGiftCount);
        var diamonds = streak.TotalDiamondCount;

        ProcessFinalGift(user, name, id, repeat, diamonds, ct);
    }

    private void FlushStaleStreaks()
    {
        List<PendingStreak> pending;
        lock (_streakGate)
        {
            pending = _streaks.FlushStale();
        }

        foreach (var p in pending)
        {
            BridgeLog.Warn(
                $"Combo sin cierre de TikTok (timeout): {p.ViewerName} " +
                $"{(!string.IsNullOrWhiteSpace(p.GiftName) ? p.GiftName : p.GiftId.ToString())} x{p.TotalGiftCount}");
            ProcessFinalGift(
                p.ViewerName,
                p.GiftName,
                p.GiftId != 0 ? p.GiftId.ToString() : "",
                Math.Max(1, p.TotalGiftCount),
                p.TotalDiamondCount,
                CancellationToken.None);
        }
    }

    private void ProcessFinalGift(string user, string name, string id, int repeat, long diamonds, CancellationToken ct)
    {
        var giftLabel = !string.IsNullOrWhiteSpace(name) ? name : id;

        if (_options.CaptureOnly)
        {
            BridgeLog.Info($"Capturado {user} {giftLabel} x{repeat}");
            return;
        }

        var tick = _goals.Contribute(name, id, repeat);
        if (tick != null)
        {
            BridgeLog.Info($"Meta {tick.Label} {tick.Count}/{tick.Need} ({Viewer(user)} +{tick.Added})");
            if (tick.Fired && !string.IsNullOrWhiteSpace(tick.Effect))
            {
                BridgeLog.Info($"Meta {tick.Label} -> {tick.Effect}");
                _ = _dispatcher.EnqueueAsync(tick.Effect, Viewer(user), $"Meta {tick.Label}", ct);
            }

            if (!tick.AlsoInstant)
            {
                return;
            }
        }

        var match = _gifts.ResolveGift(name, id, diamonds);
        if (match == null || string.IsNullOrWhiteSpace(match.EffectId))
        {
            LogUnmapped(name, id, diamonds, repeat);
            return;
        }

        if (repeat < match.MinCount)
        {
            BridgeLog.Info(
                $"Regalo {user} {giftLabel} x{repeat} ({diamonds}d) — hace falta x{match.MinCount}+ (omitido)");
            return;
        }

        const int maxRepeats = 100;
        var isSingleton = _effects.IsSingletonEffect(match.EffectId);
        var times = isSingleton ? 1 : (match.Each ? Math.Min(repeat, maxRepeats) : 1);
        var modeLabel = isSingleton
            ? "1 vez (singleton)"
            : match.Each ? $"x{times} cada uno" : "1 vez";
        BridgeLog.Info($"Regalo {user} {giftLabel} x{repeat} ({diamonds}d) -> {match.EffectId} ({modeLabel})");

        _overlay.Notify(giftLabel, id);

        for (var i = 0; i < times; i++)
        {
            var reason = times > 1
                ? $"Regalo {giftLabel} x{repeat} ({i + 1}/{times})"
                : $"Regalo {giftLabel} x{repeat}";
            _ = _dispatcher.EnqueueAsync(match.EffectId, user, reason, ct, paramOverrides: match.Params);
        }
    }

    private void LogUnmapped(string name, string id, long diamonds, int repeat)
    {
        var label = string.IsNullOrWhiteSpace(name) ? $"id {id}" : name;
        BridgeLog.Warn($"Sin mapeo: {label} id={(string.IsNullOrWhiteSpace(id) ? "?" : id)} {diamonds}d x{repeat}");

        var hintKey = $"{id}|{GiftKeyNormalizer.Normalize(name)}";
        if (!_seenUnmapped.TryAdd(hintKey, 0))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(id))
        {
            Console.WriteLine($"  -> {{ \"gift\": \"{name}\", \"id\": \"{id}\", \"effect\": \"impulse\" }}");
        }
        else
        {
            var key = string.IsNullOrWhiteSpace(name) ? id : name;
            Console.WriteLine($"  -> {{ \"gift\": \"{key}\", \"effect\": \"impulse\" }}");
        }
    }

    private static string ViewerName(UserIdentity? user) =>
        user?.Nickname is { Length: > 0 } nick ? nick :
        user?.UniqueId is { Length: > 0 } uid ? uid : "";

    private static string Viewer(string? user) => string.IsNullOrWhiteSpace(user) ? "" : user.Trim();
}
