using HiveShock.Configuration;
using HiveShock.Hosting;
using HiveShock.Live;
using HiveShock.Logging;
using HiveShock.Networking;
using Microsoft.Extensions.Hosting;
using TikTokLive;
using TikTokLive.Proto;

namespace HiveShock.Services;

public sealed class ConfigWatchService : BackgroundService
{
    private readonly GiftConfigStore _gifts;
    private readonly GiftGoalBank _goals;
    private readonly string _giftsPath;

    public ConfigWatchService(GiftConfigStore gifts, GiftGoalBank goals, LoadedGameProfile profile)
    {
        _gifts = gifts;
        _goals = goals;
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
                    _goals.ApplyDefinitions(_gifts.Snapshot.Goals);
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

public sealed class TikTokLiveHostedService : BackgroundService, ILivePort
{
    private readonly BridgeOptions _options;
    private readonly GiftCatalogStore _catalog;
    private readonly LiveEffectRouter _router;
    private readonly LivePortHub _hub;
#if DEBUG
    private readonly HiveShock.Live.LiveDiagnosticSink? _diagnostics;
#endif

    public TikTokLiveHostedService(
        BridgeOptions options,
        GiftCatalogStore catalog,
        LiveEffectRouter router,
        LivePortHub hub
#if DEBUG
        , HiveShock.Live.LiveDiagnosticSink? diagnostics = null
#endif
        )
    {
        _options = options;
        _catalog = catalog;
        _router = router;
        _hub = hub;
#if DEBUG
        _diagnostics = diagnostics;
#endif
    }

    public string Id => LivePortIds.TikTok;
    public string DisplayName => "TikTok";
    public bool IsEnabled => _options.TikTokEnabled;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.TikTokUniqueId);
    public LivePortCapability Capabilities =>
        LivePortCapability.Chat | LivePortCapability.Follow | LivePortCapability.Share |
        LivePortCapability.Likes | LivePortCapability.Gifts | LivePortCapability.Catalog;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => RunAsync(stoppingToken);

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        if (_options.DevMode)
        {
            BridgeLog.Info("Modo pruebas: canales desactivados (solo puerto local → juego)");
            return;
        }

        if (!_options.TikTokReady)
        {
            return;
        }

        _hub.Set(LivePortIds.TikTok, "TikTok", LivePortStatus.Connecting, "");
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
                _hub.Set(LivePortIds.TikTok, "TikTok", LivePortStatus.Connecting, "Reintentando…");
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

                _hub.Set(LivePortIds.TikTok, "TikTok", LivePortStatus.Error, ex.Message);
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

        _hub.Set(LivePortIds.TikTok, "TikTok", LivePortStatus.Off, "");
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
        {
            _hub.Set(LivePortIds.TikTok, "TikTok", LivePortStatus.Live, "");
            BridgeLog.Info($"TikTok conectado room={roomId}");
        };

        client.OnDisconnected += () =>
            BridgeLog.Warn("TikTok desconectado");

        client.OnReconnecting += info =>
            BridgeLog.Warn($"TikTok reconectando {info.Attempt}/{info.MaxRetries}");

        client.OnLiveEnded += _ =>
        {
            _hub.Set(LivePortIds.TikTok, "TikTok", LivePortStatus.Ended, "Live cerrado");
            BridgeLog.Info("TikTok live terminado");
        };

        client.OnGift += gift => _router.HandleTikTokGift(gift, ct);
        client.OnLike += like =>
            _router.HandleLike(ViewerName(like.User), like.LikeCount > 0 ? like.LikeCount : 1, ct);
        client.OnFollow += social => _router.HandleFollow(ViewerName(social.User), ct, LivePortIds.TikTok);
        client.OnShare += social => _router.HandleShare(ViewerName(social.User), ct);
        client.OnChat += chat => _router.HandleChat(
            ViewerName(chat.User),
            LiveEffectRouter.TikTokStableId(chat.User),
            chat.Comment ?? "",
            ct,
            LivePortIds.TikTok);

#if DEBUG
        // Catch-all: feed every raw event to the diagnostic sink.
        if (_diagnostics != null)
            client.OnEvent += diagEvt => _diagnostics.Record(diagEvt, LivePortIds.TikTok);
#endif

        await client.RunAsync(ct).ConfigureAwait(false);
    }

    private static string ViewerName(UserIdentity? user) =>
        user?.Nickname is { Length: > 0 } nick ? nick :
        user?.UniqueId is { Length: > 0 } uid ? uid : "";
}
