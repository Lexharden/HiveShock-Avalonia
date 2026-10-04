using System.Collections.Concurrent;
using HiveShock.Configuration;
using HiveShock.Hosting;
using HiveShock.Logging;
using HiveShock.Networking;
using HiveShock.Voice;
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
    private readonly ViewerGuard _guard;
    private readonly SmartVoiceManager? _voice;
    private readonly GiftStreakTracker _streaks = new();
    private readonly object _streakGate = new();
    private readonly Timer _streakFlushTimer;
    private readonly ChatCommandGate _chatGate = new();
    private readonly FollowGate _followGate;
    private readonly bool _ownsFollowGate;
    private readonly Zeldathon.ZeldathonService? _zeldathon;
    private long _followIgnored;
    private long _followIgnoredLogTicks;
    private long _guardDenied;
    private long _guardDeniedLogTicks;
    private int _followUnidentifiedWarned;
    private readonly ConcurrentDictionary<string, byte> _seenUnmapped = new();
    private long _likeBucket;

    public LiveEffectRouter(
        BridgeOptions options,
        EffectCatalog effects,
        GiftConfigStore gifts,
        GiftCatalogStore catalog,
        EffectDispatcher dispatcher,
        OverlayNotifier overlay,
        GiftGoalBank goals,
        ViewerGuard guard,
        SmartVoiceManager? voice = null,
        Zeldathon.ZeldathonService? zeldathon = null,
        FollowGate? followGate = null)
    {
        _options = options;
        _effects = effects;
        _gifts = gifts;
        _catalog = catalog;
        _dispatcher = dispatcher;
        _overlay = overlay;
        _goals = goals;
        _guard = guard;
        _voice = voice;
        _zeldathon = zeldathon;
        _ownsFollowGate = followGate == null;
        _followGate = followGate ?? new FollowGate();
        _streakFlushTimer = new Timer(
            _ => FlushStaleStreaks(),
            null,
            GiftStreakTracker.PremiumComboIdleTimeout,
            TimeSpan.FromMilliseconds(150));
    }

    public void Dispose()
    {
        _streakFlushTimer.Dispose();
        if (_ownsFollowGate)
        {
            _followGate.Dispose();
        }
    }

    public void HandleChat(ViewerIdentity viewerId, string text, CancellationToken ct)
    {
        var portId = viewerId.PortId;
        _zeldathon?.CountChat();
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

        var viewer = Viewer(viewerId.Label);
        if (!Admit(viewerId, $"Chat {prefix}{command}"))
        {
            return;
        }

        var key = ChatCommandGate.ViewerKey(portId, viewerId.StableKey);
        var admit = _chatGate.TryAdmit(key, cfg.CooldownSec, cfg.GlobalGapSec, _dispatcher.PendingChat);
        if (!admit.Allowed)
        {
            LogChatDenied(admit, viewer, prefix, command);
            return;
        }

        BridgeLog.Info($"Chat {viewer} {prefix}{command} -> {effectId}");
        _ = _dispatcher.EnqueueAsync(effectId, viewer, $"Chat {prefix}{command}", ct, fromChat: true);
    }

    /// <summary>
    /// Pregunta al guardián si este espectador puede activar un efecto (y lo anota para poder bloquearlo
    /// desde Moderación). Falso si está bloqueado o los efectos están en pausa.
    /// </summary>
    private bool Admit(ViewerIdentity viewer, string what)
    {
        var verdict = _guard.Check(viewer, what);
        if (verdict == ViewerVerdict.Allowed)
        {
            return true;
        }

        NoteDenied(verdict, viewer, what, record: false);
        return false;
    }

    /// <summary>El log se agrupa (una línea cada 5 s con el total) para que un spam no lo inunde.</summary>
    private void NoteDenied(ViewerVerdict verdict, ViewerIdentity viewer, string what, bool record = true)
    {
        if (record)
        {
            _guard.Record(viewer, what, verdict);
        }

        var count = Interlocked.Increment(ref _guardDenied);
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _guardDeniedLogTicks);
        if (now - last < 5_000 || Interlocked.CompareExchange(ref _guardDeniedLogTicks, now, last) != last)
        {
            return;
        }

        Interlocked.Exchange(ref _guardDenied, 0);
        BridgeLog.Info(verdict == ViewerVerdict.Blocked
            ? $"Bloqueado: {count} evento(s) de espectadores bloqueados ignorados (último: {viewer.Label}, {what})"
            : $"Pausado: {count} evento(s) ignorados con los efectos en pausa (último: {viewer.Label}, {what})");
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

    public void HandleFollow(ViewerIdentity viewerId, CancellationToken ct)
    {
        if (_options.CaptureOnly)
        {
            return;
        }

        // Bloqueado: ni efecto, ni voz, ni se anota como seguidor.
        if (_guard.IsBlocked(viewerId))
        {
            NoteDenied(ViewerVerdict.Blocked, viewerId, "Follow");
            return;
        }

        var isTwitch = string.Equals(viewerId.PortId, LivePortIds.Twitch, StringComparison.OrdinalIgnoreCase);
        var user = Viewer(viewerId.Label);
        if (_gifts.Snapshot.Follow.OncePerUser)
        {
            // Aunque los efectos estén en pausa se anota: seguir y dejar de seguir durante la pausa no
            // debe valer un efecto al reanudar.
            var channel = isTwitch ? _options.TwitchUserLogin : _options.TikTokUniqueId;
            switch (_followGate.Admit(viewerId, channel))
            {
                case FollowAdmitKind.Duplicate:
                    LogFollowIgnored();
                    return;
                case FollowAdmitKind.Unidentified:
                    WarnFollowUnidentified(viewerId);
                    break;
            }
        }

        _voice?.Enqueue(new TtsMessage(viewerId.PortId, user, "", DateTime.UtcNow) { Kind = TtsMessageKind.Follow, SpeakerKey = viewerId.StableKey });

        var effect = isTwitch
            ? _gifts.Snapshot.TwitchFollow.Effect
            : _gifts.Snapshot.Follow.Effect;
        if (string.IsNullOrWhiteSpace(effect))
        {
            return;
        }

        if (!Admit(viewerId, "Follow"))
        {
            return;
        }

        BridgeLog.Info($"Follow {user} -> {effect}");
        _ = _dispatcher.EnqueueAsync(effect, user, "Follow", ct);
    }

    /// <summary>Sin id ni @usuario no se puede recordar a nadie: el aviso sale una vez para no inundar el log.</summary>
    private void WarnFollowUnidentified(ViewerIdentity viewer)
    {
        if (Interlocked.Exchange(ref _followUnidentifiedWarned, 1) == 0)
        {
            BridgeLog.Warn(
                $"Follow de {viewer.Label} sin id ni @usuario: no se puede evitar que repita el efecto. " +
                "Avisa si pasa seguido.");
        }
    }

    /// <summary>Un follow repetido no dispara nada; el log se agrupa para no inundar la actividad.</summary>
    private void LogFollowIgnored()
    {
        var count = Interlocked.Increment(ref _followIgnored);
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _followIgnoredLogTicks);
        if (now - last < 10_000 || Interlocked.CompareExchange(ref _followIgnoredLogTicks, now, last) != last)
        {
            return;
        }

        BridgeLog.Info($"Follow repetido ignorado (ya disparó antes) · {count} en total");
    }

    public void HandleCheer(ViewerIdentity viewerId, int bits, CancellationToken ct)
    {
        if (_options.CaptureOnly || bits <= 0)
        {
            return;
        }

        // Bloqueado: nada de lo suyo cuenta (ni efecto, ni voz, ni tiempo de la carrera).
        if (_guard.IsBlocked(viewerId))
        {
            NoteDenied(ViewerVerdict.Blocked, viewerId, $"Bits x{bits}");
            return;
        }

        var viewer = viewerId.IsIdentified || viewerId.Name.Length > 0 ? Viewer(viewerId.Label) : "Anónimo";

        // Zeldatón: los bits pueden sumar o restar tiempo de la carrera (según la tarifa del streamer).
        _zeldathon?.Donations.OnTwitchBits(viewer, bits);

        _voice?.Enqueue(new TtsMessage(LivePortIds.Twitch, viewer, "", DateTime.UtcNow)
        {
            Kind = TtsMessageKind.Bits,
            SpeakerKey = viewerId.StableKey,
            Count = bits,
        });

        var match = _gifts.Snapshot.TwitchBits
            .Where(b => bits >= b.Min && !string.IsNullOrWhiteSpace(b.Effect))
            .MaxBy(b => b.Min);
        if (match == null)
        {
            return;
        }

        if (!Admit(viewerId, $"Bits x{bits}"))
        {
            return;
        }

        BridgeLog.Info($"Bits {viewer} x{bits} -> {match.Effect}");
        _ = _dispatcher.EnqueueAsync(match.Effect, viewer, $"Bits x{bits}", ct);
    }

    public void HandleShare(ViewerIdentity viewerId, CancellationToken ct)
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

        if (!Admit(viewerId, "Share"))
        {
            return;
        }

        var user = Viewer(viewerId.Label);
        BridgeLog.Info($"Share {user} -> {effect}");
        _ = _dispatcher.EnqueueAsync(effect, user, "Share", ct);
    }

    public void HandleLike(ViewerIdentity viewerId, int likeCount, CancellationToken ct)
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

        // Los likes son de a cientos por segundo: no se anotan uno a uno, solo cuando llegan a disparar.
        // Bloqueado o en pausa, ni siquiera suman al contador (si no, saldría todo junto al reanudar).
        if (_guard.Evaluate(viewerId) != ViewerVerdict.Allowed)
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
                var user = Viewer(viewerId.Label);
                _guard.Record(viewerId, $"{cfg.Every} likes", ViewerVerdict.Allowed);
                BridgeLog.Info($"Likes {user} x{cfg.Every} -> {cfg.Effect}");
                _ = _dispatcher.EnqueueAsync(cfg.Effect, user, $"{cfg.Every} likes", ct);
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

        if (streak.Restarted)
        {
            BridgeLog.Info(
                $"Combo reiniciado por TikTok: {ViewerName(gift.User)} {(!string.IsNullOrWhiteSpace(name) ? name : id)} " +
                $"acumulado x{streak.TotalGiftCount}");
        }

        if (!streak.IsFinal)
        {
            return;
        }

        var user = ViewerName(gift.User);
        var repeat = Math.Max(1, streak.TotalGiftCount);
        var diamonds = streak.TotalDiamondCount;

        ProcessFinalGift(ViewerIdentity.TikTok(gift.User), user, name, id, repeat, diamonds, ct);
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
                ViewerIdentity.TikTok(p.User),
                p.ViewerName,
                p.GiftName,
                p.GiftId != 0 ? p.GiftId.ToString() : "",
                Math.Max(1, p.TotalGiftCount),
                p.TotalDiamondCount,
                CancellationToken.None);
        }
    }

    private void ProcessFinalGift(
        ViewerIdentity viewerId,
        string user,
        string name,
        string id,
        int repeat,
        long diamonds,
        CancellationToken ct)
    {
        var giftLabel = !string.IsNullOrWhiteSpace(name) ? name : id;

        if (_options.CaptureOnly)
        {
            BridgeLog.Info($"Capturado {user} {giftLabel} x{repeat}");
            return;
        }

        // Bloqueado: nada de lo suyo cuenta (ni efecto, ni meta, ni voz, ni tiempo de la carrera).
        if (_guard.IsBlocked(viewerId))
        {
            NoteDenied(ViewerVerdict.Blocked, viewerId, $"Regalo {giftLabel} x{repeat}");
            return;
        }

        ReportDonationTime(user, giftLabel, id, repeat, diamonds);

        _voice?.Enqueue(new TtsMessage(LivePortIds.TikTok, Viewer(user), name, DateTime.UtcNow)
        {
            Kind = TtsMessageKind.Gift,
            SpeakerKey = user,
            Count = repeat,
            Diamonds = diamonds,
        });

        // En pausa el regalo se reconoce (voz, tiempo de la carrera) pero no suma a metas ni activa efectos.
        if (!Admit(viewerId, $"Regalo {giftLabel} x{repeat}"))
        {
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

    /// <summary>
    /// Zeldatón: el regalo (el combo entero, una vez) puede sumar o restar tiempo de la carrera. Si
    /// TikTok no mandó el precio se toma del catálogo (diamantes por unidad × cantidad).
    /// </summary>
    private void ReportDonationTime(string user, string giftLabel, string id, int repeat, long diamonds)
    {
        if (_zeldathon == null)
        {
            return;
        }

        if (diamonds <= 0 && _catalog.Find(id)?.Diamonds is > 0 and var each)
        {
            diamonds = (long)each * Math.Max(1, repeat);
        }

        _zeldathon.Donations.OnTikTokGift(Viewer(user), giftLabel, repeat, diamonds);
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
