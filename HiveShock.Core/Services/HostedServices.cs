using System.Collections.Concurrent;
using HiveShock.Configuration;
using HiveShock.Hosting;
using HiveShock.Logging;
using HiveShock.Networking;
using Microsoft.Extensions.Hosting;
using TikTokLive;
using TikTokLive.Helpers;
using TikTokLive.Proto;

namespace HiveShock.Services;

public sealed class ConfigWatchService : BackgroundService
{
    private readonly GiftConfigStore _gifts;
    private readonly string _giftsPath;

    public ConfigWatchService(GiftConfigStore gifts, LoadedGameProfile profile)
    {
        _gifts = gifts;
        _giftsPath = profile.GiftsPath;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var watcher = new FileSystemWatcher(Path.GetDirectoryName(_giftsPath)!, Path.GetFileName(_giftsPath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
        };

        var reloadGate = new object();
        var pending = false;

        void ScheduleReload()
        {
            lock (reloadGate)
            {
                if (pending)
                {
                    return;
                }

                pending = true;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(200, stoppingToken).ConfigureAwait(false);
                    _gifts.Reload(_giftsPath);
                    BridgeLog.Info($"gifts.json recargado ({_gifts.Snapshot.Groups.Count} regalos)");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    BridgeLog.Error($"gifts.json: {ex.Message}");
                }
                finally
                {
                    lock (reloadGate)
                    {
                        pending = false;
                    }
                }
            }, stoppingToken);
        }

        watcher.Changed += (_, _) => ScheduleReload();
        watcher.Created += (_, _) => ScheduleReload();
        watcher.Renamed += (_, _) => ScheduleReload();
        watcher.EnableRaisingEvents = true;

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // shut down
        }
    }
}

public sealed class EffectPumpService : BackgroundService
{
    private readonly EffectDispatcher _dispatcher;

    public EffectPumpService(EffectDispatcher dispatcher) => _dispatcher = dispatcher;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _dispatcher.RunAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // normal shutdown
        }
    }
}

public sealed class CrowdControlHostedService : BackgroundService
{
    private readonly BridgeOptions _options;
    private readonly CrowdControlServer _server;

    public CrowdControlHostedService(BridgeOptions options, CrowdControlServer server)
    {
        _options = options;
        _server = server;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.DisableCrowdControl || _options.CaptureOnly)
        {
            return;
        }

        try
        {
            await _server.RunAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // normal shutdown
        }
        catch (Exception ex)
        {
            BridgeLog.Error($"CC: {ex.Message}");
        }
    }
}

public sealed class TikTokLiveHostedService : BackgroundService
{
    private readonly BridgeOptions _options;
    private readonly EffectCatalog _effects;
    private readonly GiftConfigStore _gifts;
    private readonly GiftCatalogStore _catalog;
    private readonly EffectDispatcher _dispatcher;
    private readonly OverlayNotifier _overlay;
    private readonly GiftStreakTracker _streaks = new();
    private readonly ConcurrentDictionary<string, byte> _seenUnmapped = new();
    private long _likeBucket;

    public TikTokLiveHostedService(
        BridgeOptions options,
        EffectCatalog effects,
        GiftConfigStore gifts,
        GiftCatalogStore catalog,
        EffectDispatcher dispatcher,
        OverlayNotifier overlay)
    {
        _options = options;
        _effects = effects;
        _gifts = gifts;
        _catalog = catalog;
        _dispatcher = dispatcher;
        _overlay = overlay;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.DevMode)
        {
            BridgeLog.Info("Modo pruebas: TikTok desactivado (solo puerto local → juego)");
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.TikTokUniqueId))
        {
            BridgeLog.Error("Sin canal TikTok (TIKTOK_UNIQUE_ID).");
            return;
        }

        if (_options.CaptureOnly)
        {
            BridgeLog.Info($"Captura de regalos @{_options.TikTokUniqueId} → {_catalog.Path}");
        }
        else
        {
            BridgeLog.Info($"TikTok conectando @{_options.TikTokUniqueId}");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConnectOnceAsync(stoppingToken).ConfigureAwait(false);
                BridgeLog.Warn("TikTok desconectado. Reintento en 8s");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                // Cancelación puntual de una conexión; reintenta el bucle.
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("ttwid", StringComparison.OrdinalIgnoreCase))
                {
                    BridgeLog.Error($"TikTok ttwid: {ex.Message}");
                }
                else
                {
                    BridgeLog.Warn($"TikTok: {ex.Message}");
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(8), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ConnectOnceAsync(CancellationToken ct)
    {
        var client = new TikTokLiveClient(_options.TikTokUniqueId)
            .MaxRetries(8)
            .StaleTimeout(TimeSpan.FromSeconds(90))
            .Timeout(TimeSpan.FromSeconds(15));

        if (!string.IsNullOrWhiteSpace(_options.TikTokTtwid))
        {
            client.Ttwid(_options.TikTokTtwid);
            BridgeLog.Info("Usando TIKTOK_TTWID del .env");
        }

        if (!string.IsNullOrWhiteSpace(_options.TikTokCookies))
        {
            client.Cookies(_options.TikTokCookies);
        }

        client.OnConnected += roomId =>
            BridgeLog.Info($"TikTok conectado room={roomId}");

        client.OnDisconnected += () =>
            BridgeLog.Warn("TikTok desconectado");

        client.OnReconnecting += info =>
            BridgeLog.Warn($"TikTok reconectando {info.Attempt}/{info.MaxRetries}");

        client.OnLiveEnded += _ =>
            BridgeLog.Info("TikTok live terminado");

        client.OnGift += gift => OnGift(gift, ct);
        client.OnLike += like => OnLike(like, ct);
        client.OnFollow += social => OnFollow(social, ct);
        client.OnShare += social => OnShare(social, ct);
        client.OnChat += chat => OnChat(chat, ct);

        await client.RunAsync(ct).ConfigureAwait(false);
    }

    private void OnGift(WebcastGiftMessage gift, CancellationToken ct)
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

        var streak = _streaks.Process(gift);
        if (!streak.IsFinal)
        {
            return;
        }

        if (_options.CaptureOnly)
        {
            var label = !string.IsNullOrWhiteSpace(name) ? name : id;
            BridgeLog.Info($"Capturado {ViewerName(gift.User)} {label} x{Math.Max(1, streak.TotalGiftCount)}");
            return;
        }

        var repeat = Math.Max(1, streak.TotalGiftCount);
        var diamonds = streak.TotalDiamondCount;
        var user = ViewerName(gift.User);
        var match = _gifts.ResolveGift(name, id, diamonds);
        var giftLabel = !string.IsNullOrWhiteSpace(name) ? name : id;

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

        // each=true → un efecto por unidad del combo (x10 → 10). Tope de seguridad.
        // Singleton effects (delete_save, etc.) nunca se repiten por combo.
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
            _ = _dispatcher.EnqueueAsync(match.EffectId, user, reason, ct);
        }
    }

    private void OnLike(WebcastLikeMessage like, CancellationToken ct)
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

        var added = like.LikeCount > 0 ? like.LikeCount : 1;
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
                BridgeLog.Info($"Likes {ViewerName(like.User)} x{cfg.Every} -> {cfg.Effect}");
                _ = _dispatcher.EnqueueAsync(cfg.Effect, ViewerName(like.User), $"{cfg.Every} likes", ct);
            }
        }
    }

    private void OnFollow(WebcastSocialMessage social, CancellationToken ct)
    {
        if (_options.CaptureOnly)
        {
            return;
        }

        var effect = _gifts.Snapshot.Follow.Effect;
        if (!string.IsNullOrWhiteSpace(effect))
        {
            BridgeLog.Info($"Follow {ViewerName(social.User)} -> {effect}");
            _ = _dispatcher.EnqueueAsync(effect, ViewerName(social.User), "Follow", ct);
        }
    }

    private void OnShare(WebcastSocialMessage social, CancellationToken ct)
    {
        if (_options.CaptureOnly)
        {
            return;
        }

        var effect = _gifts.Snapshot.Share.Effect;
        if (!string.IsNullOrWhiteSpace(effect))
        {
            BridgeLog.Info($"Share {ViewerName(social.User)} -> {effect}");
            _ = _dispatcher.EnqueueAsync(effect, ViewerName(social.User), "Share", ct);
        }
    }

    private void OnChat(WebcastChatMessage chat, CancellationToken ct)
    {
        if (_options.CaptureOnly)
        {
            return;
        }

        var cfg = _gifts.Snapshot.Chat;
        if (!cfg.Enabled)
        {
            return;
        }

        var prefix = cfg.Prefix ?? "!";
        var text = (chat.Comment ?? "").Trim();
        if (text.Length == 0)
        {
            return;
        }

        if (!string.IsNullOrEmpty(prefix) && !text.StartsWith(prefix, StringComparison.Ordinal))
        {
            return;
        }

        if (!string.IsNullOrEmpty(prefix))
        {
            text = text[prefix.Length..];
        }

        var command = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault()
            ?.ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(command) || !cfg.Commands.TryGetValue(command, out var effectId))
        {
            return;
        }

        BridgeLog.Info($"Chat {ViewerName(chat.User)} {prefix}{command} -> {effectId}");
        _ = _dispatcher.EnqueueAsync(effectId, ViewerName(chat.User), $"Chat {prefix}{command}", ct);
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

        // Solo la primera vez: pista corta en consola (no al archivo de nuevo)
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
}
