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
public sealed class BridgeRuntime : IAsyncDisposable
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

    private bool _cancelHotkeyOn;

    private readonly IGameInput _keyInput = GameInputFactory.Create();
    private readonly IGlobalHotkeys _hotkeys = GameInputFactory.CreateHotkeys();

    /// <summary>Atajos de macros que no se pudieron registrar (id de macro → motivo). Vacío si todo va bien.</summary>
    public IReadOnlyDictionary<string, string> HotkeyProblems { get; private set; } = new Dictionary<string, string>();

    /// <summary>Si este sistema puede enviar teclas a otra ventana (hoy solo Windows).</summary>
    public bool SupportsKeyInput => OperatingSystem.IsWindows();

    /// <summary>Pausa de emergencia y lista de bloqueo de espectadores. Vive aunque se desconecte el puente.</summary>
    public ViewerGuard Guard { get; } = new();

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
    }

    public static BridgeRuntime Create(string[]? args = null)
    {
        var options = BridgeOptions.FromEnvironmentAndArgs(args ?? []);
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

        var effects = LoadEffects(profile, _library);
        var gifts = new GiftConfigStore(effects);
        gifts.Reload(profile.GiftsPath);

        _profile = profile;
        _effects = effects;
        _gifts = gifts;
        Options.ProfileId = profile.Id;
        Goals.ResetAll();
        Goals.ApplyDefinitions(_gifts.Snapshot.Goals);

        ApplyHotkeys();
        BridgeLog.Info($"Perfil cambiado: {profile.DisplayName} ({profile.Id}) → {Options.GameHost}:{Options.GamePort}");
        ProfileChanged?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<LoadedGameProfile> ListProfiles() => ProfileStore.ListProfiles();

    /// <summary>Crea un perfil propio (sin cambiar al actual). «Copia» parte del perfil activo.</summary>
    public LoadedGameProfile CreateProfile(string name, ProfileTemplate template)
    {
        var starterMacro = template == ProfileTemplate.Starter ? EnsureStarterMacro() : null;
        return CustomProfileStore.Create(name, template, template == ProfileTemplate.CopyOf ? _profile : null, starterMacro);
    }

    /// <summary>Quita un perfil propio. Si es el activo pasa antes a otro. Con el puente detenido.</summary>
    public void DeleteProfile(string profileId)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("Detén el bridge antes de borrar un perfil.");
        }

        var target = ProfileStore.LoadById(profileId);
        if (!target.IsCustom)
        {
            throw new InvalidOperationException("Solo se pueden borrar los perfiles propios.");
        }

        if (string.Equals(target.Id, _profile.Id, StringComparison.OrdinalIgnoreCase))
        {
            var fallback = ProfileStore.ListProfiles().FirstOrDefault(p => !p.IsCustom)
                           ?? throw new InvalidOperationException("No hay otro perfil al que cambiar.");
            SwitchProfile(fallback.Id);
        }

        CustomProfileStore.Delete(target);
        BridgeLog.Info($"Perfil propio quitado: {target.DisplayName} (queda en profiles-deleted por si fue un error)");
    }

    /// <summary>Macro de ejemplo para la plantilla básica: reutiliza la primera que exista o crea una.</summary>
    private string EnsureStarterMacro()
    {
        var existing = _library.Macros.OrderBy(m => m.Label, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        if (existing != null)
        {
            return existing.Id;
        }

        var sample = new KeyMacro
        {
            Id = "teclas_1",
            Label = "Pulsar Espacio",
            Steps = [new KeyStep { Key = "Space" }],
        };
        SaveMacroLibrary([sample], _library.Window);
        return sample.Id;
    }

    /// <summary>Macros de la biblioteca global (no dependen del juego elegido).</summary>
    public IReadOnlyList<KeyMacro> LoadKeyMacros() =>
        [.. _library.Macros.OrderBy(m => m.Label, StringComparer.OrdinalIgnoreCase)];

    /// <summary>Ventana a la que se envían las teclas (global, no por perfil).</summary>
    public WindowTarget? GameWindow => _library.Window;

    /// <summary>
    /// Guarda la biblioteca (macros + ventana) y deja las macros activas al instante, también con el puente en
    /// marcha. Si no se puede escribir en disco lanza y NO cambia lo que está activo.
    /// </summary>
    public void SaveMacroLibrary(IReadOnlyCollection<KeyMacro> macros, WindowTarget? window)
    {
        var library = new MacroLibrary
        {
            Window = window is { IsEmpty: false } ? window : null,
            Macros = [.. macros],
        };
        MacroLibraryStore.Save(library);
        _library = library;
        _effects.SetMacros(new KeyMacroCatalog(library.Macros));
        ApplyHotkeys();
    }

    /// <summary>Registra los atajos globales de las macros del perfil activo (sustituye los anteriores).</summary>
    public void ApplyHotkeys()
    {
        var problems = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var bindings = new List<HotkeyBinding>();
        foreach (var macro in _effects.Macros.Where(m => !string.IsNullOrWhiteSpace(m.Hotkey)))
        {
            if (!HotkeyCombo.TryParse(macro.Hotkey, out var combo))
            {
                problems[macro.Id] = $"Atajo «{macro.Hotkey}» no válido.";
                continue;
            }

            var id = macro.Id;
            bindings.Add(new HotkeyBinding(id, combo, () => TryRunMacro(id, "Atajo", "atajo")));
        }

        _cancelHotkeyOn = SystemEffects.IsCountingDown;
        if (_cancelHotkeyOn && HotkeyCombo.TryParse(SystemEffectService.CancelHotkey, out var cancelCombo))
        {
            bindings.Add(new HotkeyBinding("__cancelar_reinicio", cancelCombo, () => SystemEffects.Cancel("atajo de cancelar")));
        }

        foreach (var failure in _hotkeys.Apply(bindings))
        {
            problems[failure.Id] = failure.Reason;
        }

        foreach (var (id, reason) in problems)
        {
            BridgeLog.Warn($"Atajo de «{id}»: {reason}");
        }

        HotkeyProblems = problems;
    }


    public WindowStatus GetGameWindowStatus(WindowTarget window) =>
        window.IsEmpty ? WindowStatus.NotFound : _keyInput.GetStatus(window);

    /// <summary>Ventanas abiertas para elegir el juego. Vacío fuera de Windows.</summary>
    public IReadOnlyList<Platform.Windows.WindowInfo> ListGameWindows() =>
        OperatingSystem.IsWindows() ? Platform.Windows.Win32GameInput.ListWindows() : [];

    /// <summary>Prueba una macro (aunque no esté guardada). Devuelve los errores de validación; vacío = en marcha.</summary>
    public IReadOnlyList<string> TestKeyMacro(KeyMacro macro)
    {
        var errors = KeyMacroValidator.Validate(macro);
        if (errors.Count == 0)
        {
            EnsureKeyPump();
            KeyRunner.Enqueue(macro, "Prueba", "prueba");
        }

        return errors;
    }

    private static EffectCatalog LoadEffects(LoadedGameProfile profile, MacroLibrary library)
    {
        var catalog = EffectCatalog.Load(profile.EffectsPath, profile.Info, new KeyMacroCatalog(library.Macros));
        catalog.SetSystemEffects(SystemEffectIds.Supported(PowerControlFactory.Create()));
        return catalog;
    }

    /// <summary>Arranca (una sola vez) el bucle que ejecuta las macros de teclas.</summary>
    private void EnsureKeyPump()
    {
        lock (_gate)
        {
            _keyPump ??= Task.Run(() => KeyRunner.RunAsync(_keyCts.Token));
        }
    }

    /// <summary>Si el efecto es una macro de teclas la ejecuta y devuelve true (pruebas de la UI, que no pasan por el dispatcher).</summary>
    /// <summary>Efecto del sistema (reiniciar…) lanzado desde las pruebas de la UI. True si lo era.</summary>
    private bool TryRunSystemEffect(string effectId, string user, string reason)
    {
        if (!SystemEffectIds.IsSystemEffect(effectId) || !_effects.Contains(effectId))
        {
            return false;
        }

        SystemEffects.Trigger(effectId, user, reason);
        return true;
    }

    private bool TryRunMacro(string effectId, string user, string reason)
    {
        if (!_effects.TryGetMacro(effectId, out var macro))
        {
            return false;
        }

        EnsureKeyPump();
        KeyRunner.Enqueue(macro, user, reason);
        return true;
    }

    public void ReloadGifts()
    {
        _gifts.Reload(GiftsPath);
        Goals.ApplyDefinitions(_gifts.Snapshot.Goals);
    }

    public void ReloadEffectsAndGifts()
    {
        _effects = LoadEffects(_profile, _library);
        _gifts = new GiftConfigStore(_effects);
        _gifts.Reload(GiftsPath);
        Goals.ApplyDefinitions(_gifts.Snapshot.Goals);
        ProfileChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Relee profile.json del perfil activo tras editarlo (sin cambiar de perfil).</summary>
    public void ReloadProfileMeta()
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("Detén el bridge antes de editar el perfil.");
        }

        _profile = ProfileStore.LoadFromDirectory(_profile.Directory);
        ReloadEffectsAndGifts();
    }

    public void ReloadCatalog() => Catalog.Load();

    public void ResetDeathCounter() => DeathCounter.Reset();

    /// <summary>Olvida quién ya disparó el efecto de seguir. Solo con el puente detenido.</summary>
    public void ClearFollowHistory()
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("Detén el puente para borrar el historial de seguidores.");
        }

        FollowGate.ClearPersisted();
    }

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
        if (TryRunMacro(tick.Effect, "Test", "meta"))
        {
            return Task.CompletedTask;
        }

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
                if (TryRunMacro(tick.Effect, testerName, "meta"))
                {
                    BridgeLog.Info($"Meta {tick.Label} -> {tick.Effect}");
                }

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

        var isSingleton = _effects.IsSingletonEffect(gift.Effect) || SystemEffectIds.IsSystemEffect(gift.Effect);
        var times = isSingleton ? 1 : (gift.Each ? Math.Min(repeat, 100) : 1);

        BridgeLog.Info(
            $"Prueba {gift.Gift} x{repeat} → {gift.Effect} " +
            $"({(isSingleton ? "1 vez (singleton)" : gift.Each ? $"x{times} cada uno" : "1 vez")})");

        for (var i = 0; i < times; i++)
        {
            if (TryRunMacro(gift.Effect, testerName, "prueba") || TryRunSystemEffect(gift.Effect, testerName, "prueba"))
            {
                continue;
            }

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

    public void SaveGameHost(string host)
    {
        var value = host.Trim();
        if (string.IsNullOrEmpty(value))
        {
            value = "127.0.0.1";
        }

        Options.GameHost = value;
        EnvFileWriter.Upsert(EnvFileWriter.EnsureEnvPath(), "GAME_HOST", value);
    }

    public Task SendEffectAsync(string effectId, string testerName = "Test", CancellationToken ct = default)
    {
        if (TryRunMacro(effectId, testerName, "prueba"))
        {
            return Task.CompletedTask;
        }

        var cmd = _effects.Resolve(effectId, testerName)
                  ?? throw new InvalidOperationException($"Efecto desconocido: {effectId}");
        return _gameClient.SendAsync(cmd, ct);
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
        builder.Services.AddSingleton(Guard);
        builder.Services.AddSingleton(_gameClient);
        builder.Services.AddSingleton(KeyRunner);
        builder.Services.AddSingleton(SystemEffects);
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

    public async Task StopAsync()
    {
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
        BridgeLog.Info("Detenido.");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        KeyRunner.CancelAll();
        SystemEffects.Dispose();
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
            if (Environment.GetEnvironmentVariable("GAME_HOST") is null && !OperatingSystem.IsAndroid())
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
