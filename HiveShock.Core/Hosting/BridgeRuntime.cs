using HiveShock;
using HiveShock.Configuration;
using HiveShock.Input;
using HiveShock.Live;
using HiveShock.Logging;
using HiveShock.Networking;
using HiveShock.Platform;
using HiveShock.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HiveShock.Hosting;

public enum BridgeRunMode
{
    Live,
    Capture,
    Sdk,
}

/// <summary>Fachada única para CLI y GUI: perfiles, config y host.</summary>
public sealed partial class BridgeRuntime : IAsyncDisposable
{
    private readonly object _gate = new();
    private IHost? _host;
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private readonly GameTcpClient _gameClient;
    private readonly GameEventServer _events;
    private readonly CancellationTokenSource _keyCts = new();
    private Task? _keyPump;
    private EffectCatalog _effects;
    private GiftConfigStore _gifts;
    private LoadedGameProfile _profile;
    private MacroLibrary _library;

    public BridgeOptions Options { get; }
    public EffectCatalog Effects => _effects;
    public GiftConfigStore Gifts => _gifts;
    public GiftCatalogStore Catalog { get; }
    public LoadedGameProfile Profile => _profile;
    public string EffectsPath => _profile.EffectsPath;
    public string GiftsPath => _profile.GiftsPath;
    public DeathCounter DeathCounter { get; } = new();
    public OverlayNotifier Overlay { get; } = new();
    public GiftGoalBank Goals { get; } = new();
    public LivePortHub Ports { get; } = new();

    /// <summary>Teclas hacia la ventana del juego. Vive con el runtime: sirve al puente en marcha y a las pruebas de la UI.</summary>
    public KeyMacroRunner KeyRunner { get; }

    /// <summary>Reinicio del equipo como efecto (hoy solo Windows). Desarmado en cada sesión.</summary>
    public SystemEffectService SystemEffects { get; }

    /// <summary>Qué regalos y mensajes ya se vieron (sin duplicados) y qué efectos aceptados aún no llegaron al juego.</summary>
    public GiftLedger Ledger { get; } = GiftLedger.OpenDefault();

    private bool _cancelHotkeyOn;

    private readonly IGameInput _keyInput = GameInputFactory.Create();
    private readonly IGlobalHotkeys _hotkeys = GameInputFactory.CreateHotkeys();

    /// <summary>Atajos de macros que no se pudieron registrar (id de macro → motivo). Vacío si todo va bien.</summary>
    public IReadOnlyDictionary<string, string> HotkeyProblems { get; private set; } = new Dictionary<string, string>();

    /// <summary>Si este sistema puede enviar teclas a otra ventana (hoy solo Windows).</summary>
    public bool SupportsKeyInput => OperatingSystem.IsWindows();

    /// <summary>Pausa de emergencia y lista de bloqueo de espectadores. Vive aunque se desconecte el puente.</summary>
    public ViewerGuard Guard { get; } = new();

    /// <summary>Recompensas de puntos creadas por HiveShock: completar o devolver canjes, pausarlas, crearlas.</summary>
    public TwitchRewardManager RewardManager { get; }

#if DEBUG
    public Live.LiveDiagnosticSink Diagnostics { get; } = new();
#endif

    /// <summary>Compat: valor mostrado del contador de partida.</summary>
    public int DeathsThisRun => DeathCounter.Value;

    public event EventHandler? DeathsChanged
    {
        add => DeathCounter.Changed += value;
        remove => DeathCounter.Changed -= value;
    }

    public event EventHandler? ProfileChanged;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _host != null;
            }
        }
    }

    public BridgeRunMode? ActiveMode { get; private set; }

    private BridgeRuntime(
        BridgeOptions options,
        LoadedGameProfile profile,
        EffectCatalog effects,
        GiftConfigStore gifts,
        GiftCatalogStore catalog,
        MacroLibrary library)
    {
        Options = options;
        _profile = profile;
        _library = library;
        _effects = effects;
        _gifts = gifts;
        Catalog = catalog;
        _gameClient = new GameTcpClient(options);
        KeyRunner = new KeyMacroRunner(_keyInput, () => _library.Window, () => Options.DryRun);
        SystemEffects = new SystemEffectService(PowerControlFactory.Create(), () => Options.DryRun, () => Guard.IsPaused);
        // El atajo de cancelar solo existe mientras hay una cuenta atrás: no le quita una tecla al juego el resto del tiempo.
        SystemEffects.Changed += () =>
        {
            if (SystemEffects.IsCountingDown != _cancelHotkeyOn)
            {
                ApplyHotkeys();
            }
        };
        _events = new GameEventServer(options);
        _events.EventReceived += OnGameEvent;
        _events.Start();

        RewardManager = new TwitchRewardManager(options);
        // Pausa de emergencia: las recompensas de HiveShock se pausan también en Twitch (nadie gasta puntos en vano).
        Guard.PausedChanged += _ => SyncTwitchRewards();
    }

    /// <summary>
    /// Pone las recompensas de HiveShock como tocan ahora: activas (las que tienen efecto en este perfil) solo con el
    /// directo en marcha, Twitch conectado y sin pausa de emergencia; pausadas en cualquier otro caso.
    /// </summary>
    private void SyncTwitchRewards(bool refresh = false)
    {
        var live = IsRunning && ActiveMode == BridgeRunMode.Live && Options.TwitchReady && !Guard.IsPaused;
        RewardManager.RequestSync(_gifts.Snapshot.ActiveRewardIds(), live, refresh);
    }

    public static BridgeRuntime Create(string[]? args = null)
    {
        var options = BridgeOptions.FromEnvironmentAndArgs(args ?? []);
        ProfileSeeder.SeedFromApp(); // perfiles nuevos del catálogo (los que ya tiene no se tocan)
        var profile = ProfileStore.LoadActive(options.ProfileId);
        ApplyProfileToOptions(options, profile, forcePortsFromProfile: false);

        var library = MacroLibraryStore.LoadOrImport(profile);
        var effects = LoadEffects(profile, library);
        var gifts = new GiftConfigStore(effects);
        gifts.Reload(profile.GiftsPath);
        var catalog = GiftCatalogStore.LoadOrCreate();

        BridgeLog.Info($"Perfil activo: {profile.DisplayName} ({profile.Id}) → {options.GameHost}:{options.GamePort}");
        var runtime = new BridgeRuntime(options, profile, effects, gifts, catalog, library);
        runtime.Goals.ApplyDefinitions(gifts.Snapshot.Goals);
        runtime.ApplyHotkeys();

        // Si la app se cerró de golpe con el directo en marcha, sus recompensas pueden seguir activas: al abrir se
        // pausan hasta que conecte (sin cuenta, sin permiso o sin recompensas propias no hace nada).
        if (options.TwitchReady)
        {
            runtime.SyncTwitchRewards(refresh: true);
        }

        return runtime;
    }

    /// <summary>Último estado de la cola de enemigos que reportó el juego (HiveShock en Shipwright).</summary>
    public GameSpawnQueue SpawnQueue { get; private set; } = GameSpawnQueue.Empty;

    /// <summary>Cambió <see cref="SpawnQueue"/> (llega desde el hilo del lector de eventos).</summary>
    public event EventHandler? SpawnQueueChanged;

    private void OnGameEvent(object? sender, GameEventArgs e)
    {
        if (string.Equals(e.Event, "spawn_queue", StringComparison.OrdinalIgnoreCase))
        {
            if (e.Data is { } queueData && GameSpawnQueue.TryParse(queueData, out var queue))
            {
                SpawnQueue = queue;
                SpawnQueueChanged?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        if (string.Equals(e.Event, "spawn_rejected", StringComparison.OrdinalIgnoreCase))
        {
            var action = e.Data is { } rejected && rejected.TryGetProperty("action", out var a) ? a.GetString() : null;
            BridgeLog.Warn($"El juego rechazó un spawn ({action ?? "?"}): la cola de enemigos está llena.");
            return;
        }

        if (!_profile.Info.SupportsDeathEvents)
        {
            return;
        }

        if (string.Equals(e.Event, "player_death", StringComparison.OrdinalIgnoreCase))
        {
            DeathCounter.RegisterDeath();
            return;
        }

        if (string.Equals(e.Event, "save_deleted", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(e.Event, "deaths_reset", StringComparison.OrdinalIgnoreCase))
        {
            DeathCounter.Reset();
            if (string.Equals(e.Event, "save_deleted", StringComparison.OrdinalIgnoreCase))
            {
                BridgeLog.Info("Partida borrada en el juego.");
            }
        }
    }

    public async Task StartAsync(BridgeRunMode mode, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_host != null)
            {
                throw new InvalidOperationException("El bridge ya está en marcha.");
            }
        }

        ApplyMode(mode);

        if (mode is BridgeRunMode.Capture && !Options.TikTokReady)
        {
            throw new InvalidOperationException("Anotar regalos necesita el canal TikTok activo y un usuario.");
        }

        if (mode is BridgeRunMode.Live && !Options.HasAnyLivePort)
        {
            throw new InvalidOperationException("Activa al menos un canal (TikTok o Twitch) antes de conectar.");
        }

        Ports.ResetSession();
        BridgeLog.Init();
        BridgeLog.Info(
            $"Inicio mode={mode.ToString().ToLowerInvariant()} perfil={_profile.Id} " +
            $"tiktok={Options.TikTokUniqueId} twitch={Options.TwitchUserLogin} " +
            $"juego={Options.GameHost}:{Options.GamePort} dry={Options.DryRun} " +
            $"gap={Options.EffectGapMs}ms");

        var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
        builder.Services.AddSingleton(Options);
        builder.Services.AddSingleton(_profile);
        builder.Services.AddSingleton(_effects);
        builder.Services.AddSingleton(_gifts);
        builder.Services.AddSingleton(Catalog);
        builder.Services.AddSingleton(Overlay);
        builder.Services.AddSingleton(Goals);
        builder.Services.AddSingleton(Ports);
        builder.Services.AddSingleton(Guard);
        builder.Services.AddSingleton(_gameClient);
        builder.Services.AddSingleton(KeyRunner);
        builder.Services.AddSingleton(SystemEffects);
        builder.Services.AddSingleton(Ledger);
        builder.Services.AddSingleton(RewardManager);
        builder.Services.AddSingleton<ITwitchRedemptionSink>(RewardManager);
        builder.Services.AddSingleton<EffectDispatcher>();
        builder.Services.AddSingleton<LiveEffectRouter>();
        builder.Services.AddSingleton<TestServer>();
        builder.Services.AddHostedService<EffectPumpService>();
        builder.Services.AddHostedService<ConfigWatchService>();
        builder.Services.AddHostedService<TestServerHostedService>();
#if DEBUG
        builder.Services.AddSingleton(Diagnostics);
#endif
        builder.Services.AddHostedService<TikTokLiveHostedService>();
        builder.Services.AddHostedService<TwitchLiveHostedService>();

        var host = builder.Build();
        EnsureKeyPump();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        lock (_gate)
        {
            _host = host;
            _cts = cts;
            ActiveMode = mode;
            _runTask = RunHostAsync(host, cts.Token);
        }
    }

    private static async Task RunHostAsync(IHost host, CancellationToken ct)
    {
        try
        {
            await host.RunAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Normal al pulsar Desconectar / cerrar la app.
        }
    }

    public Task WaitUntilStoppedAsync()
    {
        Task? t;
        lock (_gate)
        {
            t = _runTask;
        }

        return t is null ? Task.CompletedTask : ObserveHostTaskAsync(t);
    }

    private static async Task ObserveHostTaskAsync(Task runTask)
    {
        try
        {
            await runTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // expected
        }
    }

    /// <param name="keepPending">
    /// Al cerrar la aplicación se conservan los efectos aceptados que no llegaron al juego para entregarlos al volver a abrir;
    /// al desconectar a propósito se descartan.
    /// </param>
    public async Task StopAsync(bool keepPending = false)
    {
        if (!keepPending)
        {
            Ledger.DiscardAllPending();
        }

        // Una cuenta atrás no debe sobrevivir a desconectar: quien se va del live no espera un reinicio.
        SystemEffects.Cancel("se desconectó el puente");
        IHost? host;
        CancellationTokenSource? cts;
        Task? runTask;

        lock (_gate)
        {
            host = _host;
            cts = _cts;
            runTask = _runTask;
            _host = null;
            _cts = null;
            _runTask = null;
            ActiveMode = null;
        }

        if (host == null)
        {
            return;
        }

        try
        {
            cts?.Cancel();
            await host.StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // expected during shutdown
        }
        catch (Exception ex)
        {
            BridgeLog.Warn($"Stop: {ex.Message}");
        }

        try
        {
            if (runTask != null)
            {
                await runTask.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // expected
        }
        catch (Exception ex)
        {
            BridgeLog.Warn($"Host: {ex.Message}");
        }

        host.Dispose();
        cts?.Dispose();

        // Canjes de puntos con el plan a medias y recompensas de HiveShock: se resuelven y se pausan en Twitch antes
        // de dar por terminado (sin esperar más de unos segundos si Twitch no responde).
        RewardManager.SettleOutstanding(keepPending);
        SyncTwitchRewards();
        await RewardManager.FlushAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        BridgeLog.Info("Detenido.");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(keepPending: true).ConfigureAwait(false);
        KeyRunner.CancelAll();
        SystemEffects.Dispose();
        Ledger.Dispose();
        await RewardManager.DisposeAsync().ConfigureAwait(false);
        _hotkeys.Dispose();
        await _keyCts.CancelAsync().ConfigureAwait(false);
        _events.EventReceived -= OnGameEvent;
        await _events.DisposeAsync().ConfigureAwait(false);
    }

    private static void ApplyProfileToOptions(BridgeOptions options, LoadedGameProfile profile, bool forcePortsFromProfile)
    {
        // Host/port from profile unless .env already set GAME_* (Create path) —
        // when switching profiles always force profile endpoints.
        if (forcePortsFromProfile)
        {
            options.ApplyProfileEndpoints(profile.Info, force: true);
        }
        else
        {
            // First load: profile fills defaults; .env already applied in FromEnvironmentAndArgs.
            if (Environment.GetEnvironmentVariable("GAME_HOST") is null && !MobilePlatform.IsMobile)
            {
                options.GameHost = string.IsNullOrWhiteSpace(profile.Info.GameHost)
                    ? "127.0.0.1"
                    : profile.Info.GameHost;
            }

            if (Environment.GetEnvironmentVariable("GAME_PORT") is null)
            {
                options.GamePort = profile.Info.GamePort > 0 ? profile.Info.GamePort : 43000;
            }

            if (BridgeOptions.EnvAny("HIVESHOCK_TEST_PORT", "CC_PORT") is null)
            {
                options.TestPort = profile.Info.TestPort > 0
                    ? profile.Info.TestPort
                    : 43001;
            }

            if (Environment.GetEnvironmentVariable("EVENT_PORT") is null)
            {
                options.EventPort = profile.Info.EventPort > 0 ? profile.Info.EventPort : 43002;
            }
        }

        options.ProfileId = profile.Id;
    }

    private void ApplyMode(BridgeRunMode mode)
    {
        switch (mode)
        {
            case BridgeRunMode.Live:
                Options.DevMode = false;
                Options.CaptureOnly = false;
                break;
            case BridgeRunMode.Capture:
                Options.DevMode = false;
                Options.CaptureOnly = true;
                Options.DisableTestServer = true;
                break;
            case BridgeRunMode.Sdk:
                Options.DevMode = true;
                Options.CaptureOnly = false;
                Options.DisableTestServer = false;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }
    }
}
