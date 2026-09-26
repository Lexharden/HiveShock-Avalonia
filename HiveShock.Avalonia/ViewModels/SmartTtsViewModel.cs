using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HiveShock.Voice;
using HiveShock.Voice.Piper;

namespace HiveShock.Avalonia.ViewModels;

/// <summary>Opción del desplegable "Tipo de voz": valor interno + textos para el streamer.</summary>
public sealed record EngineChoice(string Value, string Label, string Description);

/// <summary>Opción del desplegable "¿A quién se le lee?".</summary>
public sealed record AudienceChoice(TtsAudience Value, string Label);

/// <summary>
/// Página "Voz": arranca/detiene Smart TTS, elige motor, voz, velocidad, tono, volumen,
/// salida y micrófono, filtros del chat y atajos. <see cref="MainViewModel.SmartTts"/> es
/// la única instancia; Voice puede ser null (Mac/Linux todavía no tienen implementación
/// concreta — ver <see cref="MainViewModel"/>), y la página lo muestra como no disponible.
/// Todos los textos visibles están en lenguaje de streamer: nada de true/false ni códigos.
/// Cada cambio se guarda solo (con un pequeño retraso, para no escribir el archivo en
/// cada paso de un slider).
/// </summary>
public sealed partial class SmartTtsViewModel : ViewModelBase
{
    private const string DefaultDeviceLabel = "Predeterminado de Windows";

    private readonly TtsSettings _fallbackSettings = new();
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _liveTimer;
    private readonly Dictionary<string, object?> _liveSnapshot = [];
    private bool _loadingVoices;
    private bool _stateRefreshQueued;

    public SmartTtsViewModel(MainViewModel shell)
    {
        Shell = shell;
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            Voice?.Settings.Save();
        };
        _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _liveTimer.Tick += (_, _) => RefreshLiveState();

        RefreshEngineOptions();
        AudienceOptions =
        [
            new(TtsAudience.Everyone, "Todo el chat"),
            new(TtsAudience.SubscribersAndMods, "Solo suscriptores, VIP y moderadores"),
            new(TtsAudience.ModsOnly, "Solo moderadores"),
        ];

        if (Voice == null)
        {
            return;
        }

        _muteHotkey = S.MuteHotkey;
        _skipHotkey = S.SkipHotkey;
        _ignoredUsersText = string.Join(Environment.NewLine, S.IgnoredUsers);
        _blockedWordsText = string.Join(Environment.NewLine, S.BlockedWords);

        if (Voice.Engines.Find(TtsEngines.Piper) is PiperTtsEngine piper)
        {
            Piper = new PiperManagerViewModel(piper, OnEnginesChanged, Shell.Dialogs.ConfirmAsync);
        }

        Voice.StateChanged += QueueStateRefresh;
        LoadDevices();
        if (S.Enabled)
        {
            Voice.Start();
        }

        _liveTimer.Start();
        _ = InitializeServiceAsync();
    }

    public MainViewModel Shell { get; }

    private SmartVoiceManager? Voice => Shell.Runtime.Voice;
    private TtsSettings S => Voice?.Settings ?? _fallbackSettings;

    // ------------------------------------------------------------------ disponibilidad

    public bool IsAvailable => Voice != null;

    /// <summary>Microsoft bloqueó las voces en línea: la sección queda bloqueada con el aviso.</summary>
    public bool IsBlocked => Voice?.IsBlocked ?? false;

    public bool IsUnlocked => IsAvailable && !IsBlocked;
    /// <summary>Hay más de un motor para elegir (si no, el desplegable "Tipo de voz" sobra).</summary>
    public bool HasEngineChoice => EngineOptions.Count > 1;

    /// <summary>Motor sin internet al que ofrecer cambiarse si el actual queda bloqueado.</summary>
    public bool HasOfflineFallback => Voice?.Engines.OfflineFallback != null;

    public string OfflineFallbackButtonText => Voice?.Engines.OfflineFallback is { } engine
        ? $"Usar {char.ToLowerInvariant(engine.DisplayName[0])}{engine.DisplayName[1..]} (sin internet)"
        : "";

    /// <summary>False con motores que no cambian el tono (p. ej. Piper): el control se desactiva.</summary>
    public bool IsPitchSupported => Voice?.CurrentEngine.SupportsPitch ?? true;

    public bool IsCheckingService => Voice?.ServiceState == VoiceServiceState.Checking;

    public bool HasServiceProblem => Voice?.ServiceState == VoiceServiceState.Offline;
    public string ServiceProblemText => Voice?.ServiceMessage ?? "";

    /// <summary>El fallo es de librerías de Windows (voces locales): se ofrece descargar Visual C++.</summary>
    public bool ServiceNeedsVisualCpp => ServiceProblemText == HiveShock.Voice.Piper.Synthesis.PiperDependencyException.DefaultMessage;

    [RelayCommand]
    private static void OpenVisualCppDownload() =>
        PlatformShell.OpenUrl(HiveShock.Voice.Piper.Synthesis.PiperDependencyException.VisualCppRedistributableUrl);

    public string ServiceStatusText => Voice?.ServiceState switch
    {
        VoiceServiceState.Checking => "Comprobando el servicio de voces…",
        VoiceServiceState.Ready => Voice.CurrentEngine.ReadyText,
        VoiceServiceState.Offline => "Sin conexión con el servicio de voces",
        VoiceServiceState.Blocked => "Servicio de voces bloqueado",
        _ => "Servicio de voces sin comprobar",
    };

    public bool IsServiceReady => Voice?.ServiceState == VoiceServiceState.Ready;

    // ------------------------------------------------------------------ estado principal

    public bool Enabled
    {
        get => S.Enabled;
        set
        {
            if (Voice == null || S.Enabled == value)
            {
                return;
            }

            S.Enabled = value;
            if (value)
            {
                Voice.Start();
            }
            else
            {
                Voice.Stop();
            }

            OnPropertyChanged();
            ScheduleSave();
            RefreshLiveState();
        }
    }

    public bool IsRunning => Voice?.IsRunning ?? false;
    public bool IsMuted => Voice?.IsMuted ?? false;
    public bool IsStreamerSpeaking => Voice?.IsStreamerSpeaking ?? false;

    /// <summary>Resumen en una línea de lo que está pasando ahora.</summary>
    public string StatusTitle
    {
        get
        {
            if (Voice == null || !Voice.IsRunning)
            {
                return "Apagado: el chat no se lee";
            }

            if (Voice.IsMuted)
            {
                return "Silenciado: no se lee nada hasta que quites el silencio";
            }

            if (Voice.IsStreamerSpeaking && S.PauseWhenISpeak)
            {
                return "En pausa: estás hablando";
            }

            if (Voice.NowSpeaking.Length > 0)
            {
                return "Leyendo el chat";
            }

            return "Listo: esperando mensajes del chat";
        }
    }

    /// <summary>Color del indicador: verde leyendo/listo, dorado en pausa o silenciado, gris apagado.</summary>
    public bool StatusIsOk => IsRunning && !IsMuted && !(IsStreamerSpeaking && S.PauseWhenISpeak);
    public bool StatusIsPaused => IsRunning && !StatusIsOk;
    public bool StatusIsOff => !IsRunning;

    public string NowSpeakingText => Voice?.NowSpeaking is { Length: > 0 } now ? $"🔊 {now}" : "";
    public bool HasNowSpeaking => NowSpeakingText.Length > 0;

    public string QueueText => (Voice?.QueueCount ?? 0) switch
    {
        0 => "No hay mensajes en espera",
        1 => "1 mensaje en espera",
        var n => $"{n} mensajes en espera",
    };

    public string StatsText =>
        $"Leídos: {Voice?.SpokenCount ?? 0} · No leídos por los filtros: {Voice?.FilteredCount ?? 0}";

    public string LastFilteredText => Voice?.LastFilteredReason is { Length: > 0 } reason
        ? $"Último mensaje no leído: {reason}"
        : "";

    public string MuteButtonText => IsMuted ? "Quitar silencio" : "Silenciar";

    [RelayCommand]
    private void ToggleMute()
    {
        if (Voice == null)
        {
            return;
        }

        Voice.IsMuted = !Voice.IsMuted;
        RefreshLiveState();
    }

    [RelayCommand]
    private void Skip() => Voice?.SkipCurrent();

    [RelayCommand]
    private void ClearQueue() => Voice?.ClearQueue();

    // ------------------------------------------------------------------ voz

    /// <summary>Tipos de voz que se pueden usar ahora (Piper aparece al instalarlo).</summary>
    public ObservableCollection<EngineChoice> EngineOptions { get; } = [];

    /// <summary>Gestor de voces locales HD, o null si Piper no está registrado en este equipo.</summary>
    public PiperManagerViewModel? Piper { get; }

    public bool HasPiper => Piper != null;

    public EngineChoice? SelectedEngine
    {
        get => EngineOptions.FirstOrDefault(o => o.Value == Voice?.CurrentEngine.Id);
        set
        {
            if (Voice == null || value == null || value.Value == S.Engine)
            {
                return;
            }

            UseEngine(value.Value);
        }
    }

    private IReadOnlyList<VoiceProfile> _voices = [];

    /// <summary>Voces agrupadas por idioma: cabeceras (no seleccionables) + voces.</summary>
    public ObservableCollection<VoiceOption> VoiceOptions { get; } = [];

    [ObservableProperty] private VoiceOption? _selectedVoice;
    [ObservableProperty] private bool _isRefreshingVoices;
    [ObservableProperty] private string _voicesError = "";

    partial void OnSelectedVoiceChanged(VoiceOption? value)
    {
        if (Voice == null || value?.Voice == null || _loadingVoices)
        {
            return;
        }

        S.SetVoiceId(Voice.CurrentEngine.Id, value.Voice.Id);
        ScheduleSave();
    }

    // ------------------------------------------------------------------ voz por plataforma

    public bool PerPlatformVoice
    {
        get => S.PerPlatformVoice;
        set => SetSetting(S.PerPlatformVoice, value, v => S.PerPlatformVoice = v);
    }

    [ObservableProperty] private VoiceOption? _selectedTikTokVoice;
    [ObservableProperty] private VoiceOption? _selectedTwitchVoice;

    partial void OnSelectedTikTokVoiceChanged(VoiceOption? value) => SavePlatformVoice("tiktok", value);

    partial void OnSelectedTwitchVoiceChanged(VoiceOption? value) => SavePlatformVoice("twitch", value);

    private void SavePlatformVoice(string portId, VoiceOption? value)
    {
        if (Voice == null || value?.Voice == null || _loadingVoices)
        {
            return;
        }

        S.SetPlatformVoiceId(Voice.CurrentEngine.Id, portId, value.Voice.Id);
        ScheduleSave();
    }

    [RelayCommand]
    private Task TestTikTokVoice() => TestVoiceAsync("tiktok");

    [RelayCommand]
    private Task TestTwitchVoice() => TestVoiceAsync("twitch");

    /// <summary>Voz de una plataforma guardada, o la general si todavía no se eligió ninguna.</summary>
    private VoiceOption? PlatformOption(string portId)
    {
        var id = Voice == null ? "" : S.GetPlatformVoiceId(Voice.CurrentEngine.Id, portId);
        return VoiceOptions.FirstOrDefault(o => !o.IsHeader && o.Voice!.Id == id) ?? SelectedVoice;
    }

    // ------------------------------------------------------------------ respaldo

    public bool UseFallbackEngines
    {
        get => S.UseFallbackEngines;
        set => SetSetting(S.UseFallbackEngines, value, v => S.UseFallbackEngines = v);
    }

    /// <summary>"Leyendo con las voces de Windows mientras…" cuando el tipo de voz elegido falla.</summary>
    public string FallbackStatusText => Voice?.ActiveFallbackEngine is { } engine
        ? $"⚠️ Leyendo con «{engine.DisplayName}» mientras «{Voice.CurrentEngine.DisplayName}» no funciona."
        : "";

    public bool HasFallbackStatus => FallbackStatusText.Length > 0;

    // ------------------------------------------------------------------ voz aleatoria

    public bool RandomVoice
    {
        get => S.RandomVoice;
        set
        {
            if (SetSetting(S.RandomVoice, value, v => S.RandomVoice = v, nameof(RandomPoolText)))
            {
                OnPropertyChanged(nameof(FixedVoiceLabel));
            }
        }
    }

    public bool RandomVoicePerUser
    {
        get => S.RandomVoicePerUser;
        set => SetSetting(S.RandomVoicePerUser, value, v => S.RandomVoicePerUser = v);
    }

    public ObservableCollection<RandomLanguageItem> RandomLanguages { get; } = [];

    public string FixedVoiceLabel => S.RandomVoice
        ? "Voz de respaldo (se usa si no hay ninguna voz marcada para el sorteo)"
        : "Voz";

    public string RandomPoolText
    {
        get
        {
            var count = Voice?.RandomVoicePool().Count ?? 0;
            var languages = RandomLanguages.Count(l => l.IsSelected && l.IncludedCount > 0);
            return count switch
            {
                0 => "No hay ninguna voz marcada: se usará la voz de respaldo.",
                1 => "Se usará 1 voz.",
                _ => $"Se sorteará entre {count} voces de {languages} {(languages == 1 ? "idioma" : "idiomas")}.",
            };
        }
    }

    [RelayCommand]
    private void RandomOnlySpanish() => SetAllRandomLanguages(code => code == "es");

    [RelayCommand]
    private void RandomAllLanguages() => SetAllRandomLanguages(_ => true);

    [RelayCommand]
    private void RandomNoLanguages() => SetAllRandomLanguages(_ => false);

    private void SetAllRandomLanguages(Func<string, bool> selected)
    {
        _loadingVoices = true;
        foreach (var language in RandomLanguages)
        {
            language.IsSelected = selected(language.Code);
        }

        _loadingVoices = false;
        OnRandomSelectionChanged();
    }

    /// <summary>Pasa las casillas del sorteo a la config (idiomas marcados + voces desmarcadas).</summary>
    private void OnRandomSelectionChanged()
    {
        if (Voice == null || _loadingVoices)
        {
            return;
        }

        S.RandomLanguages = RandomLanguages.Where(l => l.IsSelected).Select(l => l.Code).ToList();

        // Solo se tocan las exclusiones del motor actual: las del otro motor (otros ids) se conservan.
        var shown = RandomLanguages.SelectMany(l => l.Voices).ToList();
        var shownIds = shown.Select(v => v.Voice.Id).ToHashSet(StringComparer.Ordinal);
        S.RandomExcludedVoices = S.RandomExcludedVoices
            .Where(id => !shownIds.Contains(id))
            .Concat(shown.Where(v => !v.IsIncluded).Select(v => v.Voice.Id))
            .ToList();

        OnPropertyChanged(nameof(RandomPoolText));
        ScheduleSave();
    }

    private void BuildRandomLanguages()
    {
        _loadingVoices = true;
        RandomLanguages.Clear();
        var selected = new HashSet<string>(S.RandomLanguages, StringComparer.OrdinalIgnoreCase);
        var excluded = new HashSet<string>(S.RandomExcludedVoices, StringComparer.Ordinal);
        foreach (var group in _voices
                     .GroupBy(v => VoiceLanguages.LanguageCode(v.Locale))
                     .OrderBy(g => VoiceLanguages.SortKey(g.Key))
                     .ThenBy(g => VoiceLanguages.LanguageName(g.Key), StringComparer.CurrentCultureIgnoreCase))
        {
            RandomLanguages.Add(new RandomLanguageItem(group.Key, group, selected.Contains(group.Key), excluded,
                OnRandomSelectionChanged));
        }

        _loadingVoices = false;
        OnPropertyChanged(nameof(RandomPoolText));
    }

    public double VolumePercent
    {
        get => Math.Round(S.Volume * 100);
        set
        {
            if (Voice == null || Math.Abs(value - VolumePercent) < 0.5)
            {
                return;
            }

            Voice.Volume = value / 100.0;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VolumeText));
            ScheduleSave();
        }
    }

    public string VolumeText => $"Volumen: {VolumePercent:0} %";

    public double RatePercent
    {
        get => S.RatePercent;
        set => SetSetting(S.RatePercent, (int)Math.Round(value), v => S.RatePercent = v, nameof(RateText));
    }

    public string RateText => S.RatePercent switch
    {
        0 => "Velocidad: normal",
        > 0 => $"Velocidad: {S.RatePercent} % más rápida",
        _ => $"Velocidad: {-S.RatePercent} % más lenta",
    };

    public double PitchPercent
    {
        get => S.PitchPercent;
        set => SetSetting(S.PitchPercent, (int)Math.Round(value), v => S.PitchPercent = v, nameof(PitchText));
    }

    public string PitchText => S.PitchPercent switch
    {
        0 => "Tono: normal",
        > 0 => $"Tono: {S.PitchPercent} % más agudo",
        _ => $"Tono: {-S.PitchPercent} % más grave",
    };

    [RelayCommand]
    private void ResetVoiceStyle()
    {
        RatePercent = 0;
        PitchPercent = 0;
    }

    public ObservableCollection<AudioDevice> OutputDevices { get; } = [];

    public AudioDevice? SelectedOutput
    {
        get => OutputDevices.FirstOrDefault(d => d.Id == S.OutputDeviceId) ?? OutputDevices.FirstOrDefault();
        set
        {
            if (Voice == null || value == null || value.Id == S.OutputDeviceId)
            {
                return;
            }

            S.OutputDeviceId = value.Id;
            Voice.ApplyOutputDevice();
            OnPropertyChanged();
            ScheduleSave();
        }
    }

    public string OutputError => Voice?.OutputError ?? "";

    [ObservableProperty] private string _testText = "Hola, así sonará el chat leído en voz alta.";
    [ObservableProperty] private bool _isTesting;

    [RelayCommand]
    private async Task Test()
    {
        if (Voice == null || IsTesting || string.IsNullOrWhiteSpace(TestText))
        {
            return;
        }

        await TestVoiceAsync(null).ConfigureAwait(true);
    }

    private async Task TestVoiceAsync(string? portId)
    {
        if (Voice == null || IsTesting || string.IsNullOrWhiteSpace(TestText))
        {
            return;
        }

        IsTesting = true;
        try
        {
            await Voice.PlaySampleAsync(TestText, CancellationToken.None, portId).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            if (!IsBlocked)
            {
                Shell.Dialogs.Error("Voz", ex.Message);
            }
        }
        finally
        {
            IsTesting = false;
        }
    }

    [RelayCommand]
    private async Task RefreshVoices()
    {
        if (Voice == null || IsRefreshingVoices)
        {
            return;
        }

        IsRefreshingVoices = true;
        VoicesError = "";
        try
        {
            var voices = await Voice.ListVoicesAsync(CancellationToken.None).ConfigureAwait(true);
            var savedId = S.GetVoiceId(Voice.CurrentEngine.Id);

            _loadingVoices = true;
            _voices = voices;
            VoiceOptions.Clear();
            foreach (var option in VoiceOption.Grouped(voices))
            {
                VoiceOptions.Add(option);
            }

            var options = VoiceOptions.Where(o => !o.IsHeader).ToList();
            var chosen = options.FirstOrDefault(o => o.Voice!.Id == savedId)
                         ?? options.FirstOrDefault(o => o.Voice!.Locale.Equals("es-MX", StringComparison.OrdinalIgnoreCase))
                         ?? options.FirstOrDefault(o => o.Voice!.Locale.StartsWith("es", StringComparison.OrdinalIgnoreCase))
                         ?? options.FirstOrDefault();
            _loadingVoices = false;
            // Guarda la elegida si era la primera vez (o la guardada ya no existe).
            SelectedVoice = chosen;
            _loadingVoices = true;
            SelectedTikTokVoice = PlatformOption("tiktok");
            SelectedTwitchVoice = PlatformOption("twitch");
            _loadingVoices = false;
            BuildRandomLanguages();

            if (voices.Count == 0)
            {
                VoicesError = Voice.CurrentEngine.NoVoicesMessage;
            }
        }
        catch (Exception)
        {
            if (!IsBlocked)
            {
                VoicesError = "No se pudo cargar la lista de voces. Revisa tu internet y pulsa Actualizar.";
            }
        }
        finally
        {
            _loadingVoices = false;
            IsRefreshingVoices = false;
            RefreshLiveState();
        }
    }

    // ------------------------------------------------------------------ micrófono

    public bool PauseWhenISpeak
    {
        get => S.PauseWhenISpeak;
        set
        {
            if (SetSetting(S.PauseWhenISpeak, value, v => S.PauseWhenISpeak = v))
            {
                Voice?.ApplyMicrophone();
            }
        }
    }

    public bool ReplayAfterInterruption
    {
        get => S.ReplayAfterInterruption;
        set => SetSetting(S.ReplayAfterInterruption, value, v => S.ReplayAfterInterruption = v);
    }

    public ObservableCollection<AudioDevice> Microphones { get; } = [];

    public AudioDevice? SelectedMicrophone
    {
        get => Microphones.FirstOrDefault(d => d.Id == S.MicrophoneDeviceId) ?? Microphones.FirstOrDefault();
        set
        {
            if (Voice == null || value == null || value.Id == S.MicrophoneDeviceId)
            {
                return;
            }

            S.MicrophoneDeviceId = value.Id;
            Voice.ApplyMicrophone();
            OnPropertyChanged();
            ScheduleSave();
        }
    }

    public double MicSensitivity
    {
        get => S.MicSensitivity;
        set
        {
            if (SetSetting(S.MicSensitivity, (int)Math.Round(value), v => S.MicSensitivity = v, nameof(MicSensitivityText)))
            {
                Voice?.ApplyMicrophone();
                OnPropertyChanged(nameof(MicThresholdPercent));
            }
        }
    }

    public string MicSensitivityText => S.MicSensitivity switch
    {
        >= 85 => $"Sensibilidad: {S.MicSensitivity} (muy alta: te detecta aunque hables bajito, también algo de ruido)",
        >= 60 => $"Sensibilidad: {S.MicSensitivity} (recomendada)",
        >= 30 => $"Sensibilidad: {S.MicSensitivity} (baja: hay que hablar más fuerte)",
        _ => $"Sensibilidad: {S.MicSensitivity} (muy baja: solo voz fuerte)",
    };

    public double MicLevelPercent => Math.Round((Voice?.MicLevel ?? 0) * 100);
    public double MicThresholdPercent => Math.Round((Voice?.MicThreshold ?? 0) * 100);
    public bool IsMicrophoneActive => Voice?.IsMicrophoneActive ?? false;

    public string MicStateText =>
        Voice == null || !Voice.IsMicrophoneActive
            ? MicrophoneError.Length > 0 ? "Micrófono no disponible"
            : !S.PauseWhenISpeak ? "Micrófono sin usar"
            : IsRunning ? "Micrófono apagado"
            : "Micrófono apagado (se enciende al activar la lectura)"
            : Voice.IsStreamerSpeaking ? "🎙️ Te estoy escuchando: la lectura se pausa" : "Micrófono en silencio";

    public bool IsMicTestActive => Voice?.IsMicTestActive ?? false;
    public string MicTestButtonText => IsMicTestActive ? "Terminar prueba" : "Probar micrófono";
    public string MicrophoneError => Voice?.MicrophoneError ?? "";

    [RelayCommand]
    private void ToggleMicTest()
    {
        Voice?.SetMicTest(!IsMicTestActive);
        RefreshLiveState();
    }

    public void StopMicTest()
    {
        if (IsMicTestActive)
        {
            Voice?.SetMicTest(false);
        }
    }

    [RelayCommand]
    private void RefreshDevices() => LoadDevices();

    // ------------------------------------------------------------------ qué se lee

    public bool ReadTikTok
    {
        get => S.ReadTikTok;
        set => SetSetting(S.ReadTikTok, value, v => S.ReadTikTok = v);
    }

    public bool ReadTwitch
    {
        get => S.ReadTwitch;
        set => SetSetting(S.ReadTwitch, value, v => S.ReadTwitch = v);
    }

    public bool SayUserName
    {
        get => S.SayUserName;
        set => SetSetting(S.SayUserName, value, v => S.SayUserName = v, nameof(ExampleText));
    }

    public bool SayPlatform
    {
        get => S.SayPlatform;
        set => SetSetting(S.SayPlatform, value, v => S.SayPlatform = v, nameof(ExampleText));
    }

    public string ExampleText => S.SayUserName
        ? S.SayPlatform ? "Así se oirá: «Juan en TikTok dice: hola»" : "Así se oirá: «Juan dice: hola»"
        : "Así se oirá: «hola»";

    public bool ReadGifts
    {
        get => S.ReadGifts;
        set => SetSetting(S.ReadGifts, value, v => S.ReadGifts = v);
    }

    public decimal GiftMinDiamonds
    {
        get => S.GiftMinDiamonds;
        set => SetSetting(S.GiftMinDiamonds, (int)Math.Clamp(value, 1, 100000), v => S.GiftMinDiamonds = v);
    }

    public bool ReadFollows
    {
        get => S.ReadFollows;
        set => SetSetting(S.ReadFollows, value, v => S.ReadFollows = v);
    }

    public bool ReadBits
    {
        get => S.ReadBits;
        set => SetSetting(S.ReadBits, value, v => S.ReadBits = v);
    }

    public IReadOnlyList<AudienceChoice> AudienceOptions { get; }

    public AudienceChoice? SelectedAudience
    {
        get => AudienceOptions.FirstOrDefault(o => o.Value == S.Audience);
        set
        {
            if (value != null)
            {
                SetSetting(S.Audience, value.Value, v => S.Audience = v);
            }
        }
    }

    // ------------------------------------------------------------------ filtros

    public bool IgnoreOwnMessages
    {
        get => S.IgnoreOwnMessages;
        set => SetSetting(S.IgnoreOwnMessages, value, v => S.IgnoreOwnMessages = v);
    }

    public bool SkipCommands
    {
        get => S.SkipCommands;
        set => SetSetting(S.SkipCommands, value, v => S.SkipCommands = v);
    }

    public bool SkipMentions
    {
        get => S.SkipMentions;
        set => SetSetting(S.SkipMentions, value, v => S.SkipMentions = v);
    }

    public bool RemoveLinks
    {
        get => S.RemoveLinks;
        set => SetSetting(S.RemoveLinks, value, v => S.RemoveLinks = v);
    }

    public bool ReadEmojis
    {
        get => S.ReadEmojis;
        set => SetSetting(S.ReadEmojis, value, v => S.ReadEmojis = v);
    }

    public double MaxMessageLength
    {
        get => S.MaxMessageLength;
        set => SetSetting(S.MaxMessageLength, (int)Math.Round(value), v => S.MaxMessageLength = v, nameof(MaxMessageLengthText));
    }

    public string MaxMessageLengthText => $"Leer como máximo {S.MaxMessageLength} letras de cada mensaje";

    public double MaxMessageAgeSeconds
    {
        get => S.MaxMessageAgeSeconds;
        set => SetSetting(S.MaxMessageAgeSeconds, (int)Math.Round(value), v => S.MaxMessageAgeSeconds = v, nameof(MaxMessageAgeText));
    }

    public string MaxMessageAgeText =>
        $"No leer mensajes que llevan más de {FormatSeconds(S.MaxMessageAgeSeconds)} esperando";

    public double PerUserCooldownSeconds
    {
        get => S.PerUserCooldownSeconds;
        set => SetSetting(S.PerUserCooldownSeconds, (int)Math.Round(value), v => S.PerUserCooldownSeconds = v, nameof(PerUserCooldownText));
    }

    public string PerUserCooldownText => S.PerUserCooldownSeconds == 0
        ? "Leer todos los mensajes de cada persona, sin espera"
        : $"Leer como mucho un mensaje cada {FormatSeconds(S.PerUserCooldownSeconds)} por persona";

    [ObservableProperty] private string _ignoredUsersText = "";
    [ObservableProperty] private string _blockedWordsText = "";

    partial void OnIgnoredUsersTextChanged(string value)
    {
        S.IgnoredUsers = SplitList(value);
        ScheduleSave();
    }

    partial void OnBlockedWordsTextChanged(string value)
    {
        S.BlockedWords = SplitList(value);
        ScheduleSave();
    }

    [RelayCommand]
    private void RestoreRecommendedFilters()
    {
        var defaults = new TtsSettings();
        S.IgnoreOwnMessages = defaults.IgnoreOwnMessages;
        S.SkipCommands = defaults.SkipCommands;
        S.SkipMentions = defaults.SkipMentions;
        S.RemoveLinks = defaults.RemoveLinks;
        S.ReadEmojis = defaults.ReadEmojis;
        S.MaxMessageLength = defaults.MaxMessageLength;
        S.MaxMessageAgeSeconds = defaults.MaxMessageAgeSeconds;
        S.PerUserCooldownSeconds = defaults.PerUserCooldownSeconds;
        S.Audience = defaults.Audience;
        IgnoredUsersText = string.Join(Environment.NewLine, TtsSettings.DefaultIgnoredUsers.Union(S.IgnoredUsers, StringComparer.OrdinalIgnoreCase));
        foreach (var name in new[]
                 {
                     nameof(IgnoreOwnMessages), nameof(SkipCommands), nameof(SkipMentions), nameof(RemoveLinks), nameof(ReadEmojis),
                     nameof(MaxMessageLength), nameof(MaxMessageLengthText), nameof(MaxMessageAgeSeconds),
                     nameof(MaxMessageAgeText), nameof(PerUserCooldownSeconds), nameof(PerUserCooldownText),
                     nameof(SelectedAudience),
                 })
        {
            OnPropertyChanged(name);
        }

        ScheduleSave();
    }

    // ------------------------------------------------------------------ atajos

    [ObservableProperty] private string _muteHotkey = "Control+Alt+M";
    [ObservableProperty] private string _skipHotkey = "Control+Alt+N";
    [ObservableProperty] private string _hotkeyStatus = "";

    public string HotkeyError => Voice?.HotkeyError ?? "";

    [RelayCommand]
    private void SaveHotkeys()
    {
        if (Voice == null)
        {
            return;
        }

        try
        {
            Voice.UpdateHotkeys(MuteHotkey, SkipHotkey);
            Voice.Settings.Save();
            HotkeyStatus = Voice.IsRunning
                ? "Atajos guardados y activos."
                : "Atajos guardados. Funcionarán cuando actives la lectura del chat.";
        }
        catch (InvalidOperationException ex)
        {
            HotkeyStatus = "";
            Shell.Dialogs.Error("Atajos de teclado", ex.Message);
        }
    }

    // ------------------------------------------------------------------ servicio bloqueado / sin conexión

    public string BlockedTitle => "La voz está en pausa por un bloqueo de Microsoft";

    public string BlockedMessage
    {
        get
        {
            const string intro =
                "Microsoft bloqueó el acceso a su servicio de voces en línea, que es el que usa HiveShock para leer el chat. " +
                "No es un problema de tu equipo, de tu internet ni de tu cuenta de TikTok o Twitch.\n\n";
            var fallback = Voice?.Engines.OfflineFallback;
            if (fallback != null && S.UseFallbackEngines)
            {
                return intro +
                       $"Para que tu directo no se quede mudo, el chat se sigue leyendo con «{fallback.DisplayName}». " +
                       "Busca una actualización de HiveShock (lo solucionaremos en cuanto sea posible) o cámbiate a ese " +
                       "tipo de voz para seguir ajustando la voz.";
            }

            return intro +
                   "Para que tu directo no se vea afectado, la lectura del chat se detuvo sola. Busca una actualización de " +
                   "HiveShock (lo solucionaremos en cuanto sea posible)" +
                   (fallback != null ? $" o, mientras tanto, usa «{fallback.DisplayName}», que funcionan sin internet." : ".");
        }
    }

    [RelayCommand]
    private void UseOfflineVoices()
    {
        if (Voice?.Engines.OfflineFallback is { } engine)
        {
            UseEngine(engine.Id);
        }
    }

    [RelayCommand]
    private async Task CheckService()
    {
        if (Voice == null)
        {
            return;
        }

        await Voice.CheckServiceAsync(CancellationToken.None).ConfigureAwait(true);
        RefreshLiveState();
        if (Voice.ServiceState == VoiceServiceState.Ready && _voices.Count == 0)
        {
            await RefreshVoices().ConfigureAwait(true);
        }
    }

    // ------------------------------------------------------------------ internos

    private async Task InitializeServiceAsync()
    {
        if (Piper != null)
        {
            _ = Piper.InitializeAsync();
        }

        await CheckService().ConfigureAwait(true);
        if (_voices.Count == 0 && !IsBlocked)
        {
            await RefreshVoices().ConfigureAwait(true);
        }
    }

    private void RefreshEngineOptions()
    {
        var available = Voice?.Engines.Available ?? [];
        EngineOptions.Clear();
        foreach (var engine in available)
        {
            EngineOptions.Add(new EngineChoice(engine.Id, engine.DisplayName, engine.Description));
        }

        OnPropertyChanged(nameof(HasEngineChoice));
        OnPropertyChanged(nameof(SelectedEngine));
    }

    /// <summary>Se instaló/borró Piper o una voz: cambia qué tipos de voz hay y, si es el actual, sus voces.</summary>
    private void OnEnginesChanged()
    {
        if (Voice == null)
        {
            return;
        }

        RefreshEngineOptions();
        OnPropertyChanged(nameof(HasOfflineFallback));
        OnPropertyChanged(nameof(OfflineFallbackButtonText));
        OnPropertyChanged(nameof(BlockedMessage));
        if (string.Equals(Voice.CurrentEngine.Id, TtsEngines.Piper, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(S.Engine, TtsEngines.Piper, StringComparison.OrdinalIgnoreCase))
        {
            UseEngine(Voice.CurrentEngine.Id, persist: false);
        }
    }

    private void UseEngine(string engine) => UseEngine(engine, persist: true);

    private void UseEngine(string engine, bool persist)
    {
        if (Voice == null)
        {
            return;
        }

        if (persist)
        {
            S.Engine = engine;
            ScheduleSave();
        }

        _voices = [];
        VoiceOptions.Clear();
        RandomLanguages.Clear();
        SelectedVoice = null;
        OnPropertyChanged(nameof(RandomPoolText));
        OnPropertyChanged(nameof(SelectedEngine));
        OnPropertyChanged(nameof(IsPitchSupported));
        RefreshLiveState();
        _ = InitializeServiceAsync();
    }

    private void LoadDevices()
    {
        if (Voice == null)
        {
            return;
        }

        Fill(Microphones, Voice.ListMicrophones());
        Fill(OutputDevices, Voice.ListOutputs());
        OnPropertyChanged(nameof(SelectedMicrophone));
        OnPropertyChanged(nameof(SelectedOutput));

        static void Fill(ObservableCollection<AudioDevice> target, IReadOnlyList<AudioDevice> devices)
        {
            target.Clear();
            target.Add(new AudioDevice("", DefaultDeviceLabel));
            foreach (var device in devices)
            {
                target.Add(device);
            }
        }
    }

    /// <summary>Aplica un ajuste simple, avisa a la vista y programa el guardado. True si cambió.</summary>
    private bool SetSetting<T>(T current, T value, Action<T> apply, string? alsoNotify = null,
        [CallerMemberName] string? name = null)
    {
        if (Voice == null || EqualityComparer<T>.Default.Equals(current, value))
        {
            return false;
        }

        apply(value);
        OnPropertyChanged(name);
        if (alsoNotify != null)
        {
            OnPropertyChanged(alsoNotify);
        }

        ScheduleSave();
        return true;
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void QueueStateRefresh()
    {
        if (_stateRefreshQueued)
        {
            return;
        }

        _stateRefreshQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _stateRefreshQueued = false;
            RefreshLiveState();
        });
    }

    /// <summary>Avisa a la vista solo de lo que cambió desde la última vez (se llama cada 120 ms).</summary>
    private void RefreshLiveState()
    {
        Notify(nameof(IsBlocked), IsBlocked);
        Notify(nameof(IsUnlocked), IsUnlocked);
        Notify(nameof(IsCheckingService), IsCheckingService);
        Notify(nameof(HasServiceProblem), HasServiceProblem);
        Notify(nameof(ServiceProblemText), ServiceProblemText);
        Notify(nameof(ServiceNeedsVisualCpp), ServiceNeedsVisualCpp);
        Notify(nameof(ServiceStatusText), ServiceStatusText);
        Notify(nameof(IsServiceReady), IsServiceReady);
        Notify(nameof(IsRunning), IsRunning);
        Notify(nameof(IsMuted), IsMuted);
        Notify(nameof(MuteButtonText), MuteButtonText);
        Notify(nameof(IsStreamerSpeaking), IsStreamerSpeaking);
        Notify(nameof(StatusTitle), StatusTitle);
        Notify(nameof(StatusIsOk), StatusIsOk);
        Notify(nameof(StatusIsPaused), StatusIsPaused);
        Notify(nameof(StatusIsOff), StatusIsOff);
        Notify(nameof(NowSpeakingText), NowSpeakingText);
        Notify(nameof(HasNowSpeaking), HasNowSpeaking);
        Notify(nameof(QueueText), QueueText);
        Notify(nameof(StatsText), StatsText);
        Notify(nameof(LastFilteredText), LastFilteredText);
        Notify(nameof(MicLevelPercent), MicLevelPercent);
        Notify(nameof(MicThresholdPercent), MicThresholdPercent);
        Notify(nameof(IsMicrophoneActive), IsMicrophoneActive);
        Notify(nameof(MicStateText), MicStateText);
        Notify(nameof(IsMicTestActive), IsMicTestActive);
        Notify(nameof(MicTestButtonText), MicTestButtonText);
        Notify(nameof(MicrophoneError), MicrophoneError);
        Notify(nameof(OutputError), OutputError);
        Notify(nameof(HotkeyError), HotkeyError);
        Notify(nameof(SelectedEngine), SelectedEngine?.Value);
        Notify(nameof(FallbackStatusText), FallbackStatusText);
        Notify(nameof(HasFallbackStatus), HasFallbackStatus);
    }

    private void Notify(string name, object? value)
    {
        if (_liveSnapshot.TryGetValue(name, out var previous) && Equals(previous, value))
        {
            return;
        }

        _liveSnapshot[name] = value;
        OnPropertyChanged(name);
    }

    private static List<string> SplitList(string text) =>
        text.Split(['\n', '\r', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string FormatSeconds(int seconds) =>
        seconds < 60 ? $"{seconds} segundos"
        : seconds % 60 == 0 ? (seconds == 60 ? "1 minuto" : $"{seconds / 60} minutos")
        : $"{seconds / 60} min {seconds % 60} s";
}
