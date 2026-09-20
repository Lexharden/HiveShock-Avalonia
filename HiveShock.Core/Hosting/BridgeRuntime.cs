using HiveShock;
using HiveShock.Configuration;
using HiveShock.Live;
using HiveShock.Logging;
using HiveShock.Networking;
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
public sealed class BridgeRuntime : IAsyncDisposable
{
    private readonly object _gate = new();
    private IHost? _host;
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private readonly GameTcpClient _gameClient;
    private readonly GameEventServer _events;
    private EffectCatalog _effects;
    private GiftConfigStore _gifts;
    private LoadedGameProfile _profile;

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
        GiftCatalogStore catalog)
    {
        Options = options;
        _profile = profile;
        _effects = effects;
        _gifts = gifts;
        Catalog = catalog;
        _gameClient = new GameTcpClient(options);
        _events = new GameEventServer(options);
        _events.EventReceived += OnGameEvent;
        _events.Start();
    }

    public static BridgeRuntime Create(string[]? args = null)
    {
        var options = BridgeOptions.FromEnvironmentAndArgs(args ?? []);
        var profile = ProfileStore.LoadActive(options.ProfileId);
        ApplyProfileToOptions(options, profile, forcePortsFromProfile: false);

        var effects = EffectCatalog.Load(profile.EffectsPath, profile.Info);
        var gifts = new GiftConfigStore(effects);
        gifts.Reload(profile.GiftsPath);
        var catalog = GiftCatalogStore.LoadOrCreate();

        BridgeLog.Info($"Perfil activo: {profile.DisplayName} ({profile.Id}) → {options.GameHost}:{options.GamePort}");
        var runtime = new BridgeRuntime(options, profile, effects, gifts, catalog);
        runtime.Goals.ApplyDefinitions(gifts.Snapshot.Goals);
        return runtime;
    }

    /// <summary>Cambia de perfil. Debe estar detenido el host.</summary>
    public void SwitchProfile(string profileId)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("Detén el bridge antes de cambiar de perfil.");
        }

        var profile = ProfileStore.LoadById(profileId);
        ProfileStore.SaveActiveId(profile.Id);
        ApplyProfileToOptions(Options, profile, forcePortsFromProfile: true);

        var effects = EffectCatalog.Load(profile.EffectsPath, profile.Info);
        var gifts = new GiftConfigStore(effects);
        gifts.Reload(profile.GiftsPath);

        _profile = profile;
        _effects = effects;
        _gifts = gifts;
        Options.ProfileId = profile.Id;
        Goals.ResetAll();
        Goals.ApplyDefinitions(_gifts.Snapshot.Goals);

        BridgeLog.Info($"Perfil cambiado: {profile.DisplayName} ({profile.Id}) → {Options.GameHost}:{Options.GamePort}");
        ProfileChanged?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<LoadedGameProfile> ListProfiles() => ProfileStore.ListProfiles();

    public void ReloadGifts()
    {
        _gifts.Reload(GiftsPath);
        Goals.ApplyDefinitions(_gifts.Snapshot.Goals);
    }

    public void ReloadEffectsAndGifts()
    {
        _effects = EffectCatalog.Load(EffectsPath, _profile.Info);
        _gifts = new GiftConfigStore(_effects);
        _gifts.Reload(GiftsPath);
        Goals.ApplyDefinitions(_gifts.Snapshot.Goals);
        ProfileChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ReloadCatalog() => Catalog.Load();

    public void ResetDeathCounter() => DeathCounter.Reset();

    public Task SimulateGoalAsync(string goalId, int units = 1, CancellationToken ct = default)
    {
        var snap = Goals.Snapshots().FirstOrDefault(g =>
            string.Equals(g.Id, goalId, StringComparison.OrdinalIgnoreCase));
        if (snap == null)
        {
            throw new InvalidOperationException("No hay esa meta.");
        }

        var key = snap.Keys.FirstOrDefault() ?? snap.Label;
        var tick = Goals.Contribute(key, key, Math.Max(1, units));
        if (tick == null)
        {
            return Task.CompletedTask;
        }

        BridgeLog.Info($"Meta {tick.Label} {tick.Count}/{tick.Need} (prueba +{tick.Added})");
        if (!tick.Fired || string.IsNullOrWhiteSpace(tick.Effect))
        {
            return Task.CompletedTask;
        }

        BridgeLog.Info($"Meta {tick.Label} -> {tick.Effect}");
        return _gameClient.SendAsync(
            _effects.Resolve(tick.Effect, "Test") ??
            throw new InvalidOperationException($"Efecto desconocido: {tick.Effect}"),
            ct);
    }

    public Task DeleteSaveAsync(CancellationToken ct = default)
    {
        if (!_profile.Info.SupportsDeleteSave)
        {
            throw new InvalidOperationException(
                $"El perfil {_profile.DisplayName} no soporta borrar partida.");
        }

        return _gameClient.SendActionAsync("delete_save", ct);
    }

    public Task RescueAsync(CancellationToken ct = default)
    {
        if (!_profile.Info.SupportsRescue)
        {
            throw new InvalidOperationException(
                $"El perfil {_profile.DisplayName} no soporta rescatar.");
        }

        return _gameClient.SendActionAsync("rescue", ct);
    }

    /// <summary>Dispara el efecto de un mapeo (respeta Mín x / Cada uno / singleton ×1).</summary>
    public async Task TestGiftAsync(
        EditableGift gift,
        int comboCount = 1,
        string testerName = "Test",
        CancellationToken ct = default)
    {
        if (gift == null)
        {
            throw new ArgumentException("El regalo no tiene efecto.");
        }

        var repeat = Math.Max(1, comboCount);
        var tick = Goals.Contribute(gift.Gift, gift.Id, repeat);
        if (tick != null)
        {
            BridgeLog.Info($"Meta {tick.Label} {tick.Count}/{tick.Need} ({testerName} +{tick.Added})");
            if (tick.Fired && !string.IsNullOrWhiteSpace(tick.Effect))
            {
                var goalCmd = _effects.Resolve(tick.Effect, testerName);
                if (goalCmd != null)
                {
                    BridgeLog.Info($"Meta {tick.Label} -> {tick.Effect}");
                    await _gameClient.SendAsync(goalCmd, ct).ConfigureAwait(false);
                    BridgeLog.Info($"OK prueba {tick.Effect} (meta)");
                }
            }

            if (!tick.AlsoInstant)
            {
                Overlay.Notify(gift.Gift, gift.Id);
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(gift.Effect))
        {
            throw new ArgumentException("El regalo no tiene efecto.");
        }

        if (!_effects.Contains(gift.Effect))
        {
            throw new ArgumentException($"Efecto desconocido: {gift.Effect}");
        }

        var minCount = Math.Max(1, gift.MinCount);
        if (repeat < minCount)
        {
            BridgeLog.Info($"Prueba omitida: x{repeat} < Mín x{minCount} ({gift.Gift})");
            return;
        }

        var isSingleton = _effects.IsSingletonEffect(gift.Effect);
        var times = isSingleton ? 1 : (gift.Each ? Math.Min(repeat, 100) : 1);

        BridgeLog.Info(
            $"Prueba {gift.Gift} x{repeat} → {gift.Effect} " +
            $"({(isSingleton ? "1 vez (singleton)" : gift.Each ? $"x{times} cada uno" : "1 vez")})");

        for (var i = 0; i < times; i++)
        {
            var overrides = gift.Params.Count > 0 ? gift.Params : null;
            var cmd = _effects.Resolve(gift.Effect, testerName, overrides);
            if (cmd is null)
            {
                throw new InvalidOperationException($"No se pudo resolver el efecto {gift.Effect}");
            }

            await _gameClient.SendAsync(cmd, ct).ConfigureAwait(false);
            BridgeLog.Info($"OK prueba {gift.Effect} ({i + 1}/{times})");
        }

        Overlay.Notify(gift.Gift, gift.Id);
    }

    private void OnGameEvent(object? sender, GameEventArgs e)
    {
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

    public void SaveChannel(string uniqueId)
    {
        var normalized = BridgeOptions.NormalizeUniqueId(uniqueId);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Usuario inválido.");
        }

        Options.TikTokUniqueId = normalized;
        EnvFileWriter.Upsert(EnvFileWriter.EnsureEnvPath(), "TIKTOK_UNIQUE_ID", normalized);
    }

    public void SetTikTokEnabled(bool enabled)
    {
        Options.TikTokEnabled = enabled;
        EnvFileWriter.Upsert(EnvFileWriter.EnsureEnvPath(), "TIKTOK_ENABLED", enabled ? "1" : "0");
    }

    public void SetTwitchEnabled(bool enabled)
    {
        Options.TwitchEnabled = enabled;
        EnvFileWriter.Upsert(EnvFileWriter.EnsureEnvPath(), "TWITCH_ENABLED", enabled ? "1" : "0");
    }

    public void SaveTwitchClientId(string clientId)
    {
        Options.TwitchClientId = clientId.Trim();
        EnvFileWriter.Upsert(EnvFileWriter.EnsureEnvPath(), "TWITCH_CLIENT_ID", Options.TwitchClientId);
    }

    public async Task LoginTwitchAsync(IProgress<TwitchDeviceStart>? progress, CancellationToken ct)
    {
        var clientId = Options.ResolvedTwitchClientId();
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException("Twitch no está disponible en esta copia.");
        }

        using var auth = new TwitchAuthClient();
        var start = await auth.StartDeviceLoginAsync(clientId, ct).ConfigureAwait(false);
        progress?.Report(start);
        try
        {
            PlatformShell.OpenUrl(start.VerificationUri);
        }
        catch
        {
            // el usuario puede abrir la URL a mano
        }

        var tokens = await auth.WaitForDeviceTokenAsync(clientId, start, ct).ConfigureAwait(false);
        var user = await auth.GetUserAsync(clientId, tokens.AccessToken, ct).ConfigureAwait(false);
        TwitchSessionStore.Persist(Options, clientId, tokens.AccessToken, tokens.RefreshToken, user.Login, user.Id);
        BridgeLog.Info($"Twitch cuenta @{user.Login}");
        Options.TwitchEnabled = true;
        EnvFileWriter.Upsert(EnvFileWriter.EnsureEnvPath(), "TWITCH_ENABLED", "1");
    }

    public void LogoutTwitch()
    {
        TwitchSessionStore.Clear(Options);
        BridgeLog.Info("Twitch: sesión cerrada.");
    }

    public void SetDryRun(bool value) => Options.DryRun = value;

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
        builder.Services.AddSingleton(_gameClient);
        builder.Services.AddSingleton<EffectDispatcher>();
        builder.Services.AddSingleton<LiveEffectRouter>();
        builder.Services.AddSingleton<CrowdControlServer>();
        builder.Services.AddHostedService<EffectPumpService>();
        builder.Services.AddHostedService<ConfigWatchService>();
        builder.Services.AddHostedService<CrowdControlHostedService>();
#if DEBUG
        builder.Services.AddSingleton(Diagnostics);
#endif
        builder.Services.AddHostedService<TikTokLiveHostedService>();
        builder.Services.AddHostedService<TwitchLiveHostedService>();

        var host = builder.Build();
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

    public async Task StopAsync()
    {
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
        BridgeLog.Info("Detenido.");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
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
            if (Environment.GetEnvironmentVariable("GAME_HOST") is null)
            {
                options.GameHost = string.IsNullOrWhiteSpace(profile.Info.GameHost)
                    ? "127.0.0.1"
                    : profile.Info.GameHost;
            }

            if (Environment.GetEnvironmentVariable("GAME_PORT") is null)
            {
                options.GamePort = profile.Info.GamePort > 0 ? profile.Info.GamePort : 43000;
            }

            if (Environment.GetEnvironmentVariable("CC_PORT") is null)
            {
                options.CrowdControlPort = profile.Info.CrowdControlPort > 0
                    ? profile.Info.CrowdControlPort
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
                Options.DisableCrowdControl = true;
                break;
            case BridgeRunMode.Sdk:
                Options.DevMode = true;
                Options.CaptureOnly = false;
                Options.DisableCrowdControl = false;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }
    }
}
