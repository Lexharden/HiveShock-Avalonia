using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HiveShock.Avalonia.Services;
using HiveShock.Avalonia.Views.Dialogs;
using HiveShock.Hosting;
using HiveShock.Live;
using HiveShock.Logging;

namespace HiveShock.Avalonia.ViewModels;

public sealed partial class StudioViewModel : ViewModelBase
{
    private readonly MainViewModel _shell;
    private bool _loading;

    public StudioViewModel(MainViewModel shell)
    {
        _shell = shell;
        _channel = shell.Runtime.Options.TikTokUniqueId;
        _tikTokEnabled = shell.Runtime.Options.TikTokEnabled;
        _twitchEnabled = shell.Runtime.Options.TwitchEnabled;
        _twitchLogin = shell.Runtime.Options.TwitchUserLogin;
        _twitchClientId = shell.Runtime.Options.TwitchClientId;
        _simulate = shell.Runtime.Options.DryRun;
        _selectedMode = BridgeRunMode.Live;
        RefreshProfiles();
        RefreshCapabilities();
        LoadDeathPrefs();
        RefreshStats();
        RefreshPortCards();
        shell.Runtime.Ports.Changed += () =>
            global::Avalonia.Threading.Dispatcher.UIThread.Post(RefreshPortCards);
    }

    public IReadOnlyList<ProfileCardViewModel> Profiles { get; private set; } = [];

    [ObservableProperty] private string _channel = "";
    [ObservableProperty] private BridgeRunMode _selectedMode = BridgeRunMode.Live;
    [ObservableProperty] private bool _simulate;
    [ObservableProperty] private string _profileHint = "";
    [ObservableProperty] private string _gameStatus = "";
    [ObservableProperty] private string _mappingSummary = "";
    [ObservableProperty] private bool _supportsDeath;
    [ObservableProperty] private bool _canRescue;
    [ObservableProperty] private bool _canDeleteSave;
    [ObservableProperty] private bool _hasGameTools;
    [ObservableProperty] private string _rescueLabel = "Rescatar";
    [ObservableProperty] private string _deleteSaveLabel = "Borrar partida";
    [ObservableProperty] private bool _livesMode;
    [ObservableProperty] private string _deathStartText = "0";
    [ObservableProperty] private string _counterDisplay = "";
    [ObservableProperty] private bool _showCounterOnStream;
    [ObservableProperty] private bool _showGiftsOnStream;
    [ObservableProperty] private bool _showGoalsOnStream;
    [ObservableProperty] private double _overlayScale = 1.5;
    [ObservableProperty] private string _counterTitleText = "";
    [ObservableProperty] private string _giftsTitleText = "Regalos";
    [ObservableProperty] private bool _showGiftsTitle = true;
    [ObservableProperty] private bool _overlayShowBackground = true;
    [ObservableProperty] private double _overlayBackgroundOpacity = 0.94;
    [ObservableProperty] private double _overlayNumberSize = 72;
    [ObservableProperty] private double _overlayTitleSize = 16;
    [ObservableProperty] private double _overlayGiftNameSize = 14;
    [ObservableProperty] private string _overlayNumberColor = "";
    [ObservableProperty] private string _overlayTitleColor = "";
    [ObservableProperty] private string _overlayGiftColor = "";
    [ObservableProperty] private bool _overlayAlignCenter = true;
    [ObservableProperty] private string _connectLabel = "Conectar";
    [ObservableProperty] private bool _tikTokEnabled = true;
    [ObservableProperty] private bool _twitchEnabled = true;
    [ObservableProperty] private string _twitchLogin = "";
    [ObservableProperty] private string _twitchClientId = "";
    [ObservableProperty] private string _tikTokPortStatus = "Apagado";
    [ObservableProperty] private string _twitchPortStatus = "Apagado";
    [ObservableProperty] private bool _tikTokLive;
    [ObservableProperty] private bool _tikTokConnecting;
    [ObservableProperty] private bool _tikTokError;
    [ObservableProperty] private bool _twitchBusy;

    public bool CanEditSetup => !_shell.IsRunning;
    public bool CanConnect => !_shell.IsBusy;
    public bool IsToneConnecting => _shell.StatusTone == "connecting";
    public bool IsToneLive => _shell.StatusTone == "live";
    public bool IsToneOk => _shell.StatusTone == "ok";

    public bool IsModeLive
    {
        get => SelectedMode == BridgeRunMode.Live;
        set { if (value) SelectedMode = BridgeRunMode.Live; }
    }

    public bool IsModeCapture
    {
        get => SelectedMode == BridgeRunMode.Capture;
        set { if (value) SelectedMode = BridgeRunMode.Capture; }
    }

    public bool IsModeSdk
    {
        get => SelectedMode == BridgeRunMode.Sdk;
        set { if (value) SelectedMode = BridgeRunMode.Sdk; }
    }

    public bool CountUpMode
    {
        get => !LivesMode;
        set { if (value) LivesMode = false; }
    }

    public bool OverlayAlignLeft
    {
        get => !OverlayAlignCenter;
        set { if (value) OverlayAlignCenter = false; }
    }

    public bool OverlayTitleIsDeaths
    {
        get => !OverlayTitleIsLives;
        set { if (value) OverlayTitleIsLives = false; }
    }

    public bool OverlayTitleIsLives
    {
        get
        {
            var title = string.IsNullOrWhiteSpace(CounterTitleText)
                ? (LivesMode ? "Vidas" : "Muertes")
                : CounterTitleText.Trim();
            return title.Equals("Vidas", StringComparison.OrdinalIgnoreCase);
        }
        set
        {
            CounterTitleText = value ? "Vidas" : "Muertes";
        }
    }

    public bool IsNumberColorTheme => string.IsNullOrWhiteSpace(OverlayNumberColor);
    public bool IsNumberColorGold => OverlayNumberColor == OverlayLook.Gold;
    public bool IsNumberColorBlue => OverlayNumberColor == OverlayLook.Blue;
    public bool IsNumberColorWhite => OverlayNumberColor == OverlayLook.White;
    public bool IsNumberColorBlack => OverlayNumberColor == OverlayLook.Black;
    public bool IsTitleColorTheme => string.IsNullOrWhiteSpace(OverlayTitleColor);
    public bool IsTitleColorGold => OverlayTitleColor == OverlayLook.Gold;
    public bool IsTitleColorWhite => OverlayTitleColor == OverlayLook.White;
    public bool IsTitleColorBlack => OverlayTitleColor == OverlayLook.Black;
    public bool IsGiftColorTheme => string.IsNullOrWhiteSpace(OverlayGiftColor);
    public bool IsGiftColorGold => OverlayGiftColor == OverlayLook.Gold;
    public bool IsGiftColorWhite => OverlayGiftColor == OverlayLook.White;
    public bool IsGiftColorBlack => OverlayGiftColor == OverlayLook.Black;

    public string PreviewCounterValue => _shell.Runtime.DeathCounter.Value.ToString();
    public global::Avalonia.Layout.HorizontalAlignment PreviewAlign =>
        OverlayAlignCenter
            ? global::Avalonia.Layout.HorizontalAlignment.Center
            : global::Avalonia.Layout.HorizontalAlignment.Left;
    public global::Avalonia.Media.IBrush PreviewNumberBrush =>
        new global::Avalonia.Media.SolidColorBrush(OverlayLook.Parse(OverlayNumberColor, Themes.ThemeManager.OverlayText(Themes.ThemeManager.Current)));
    public global::Avalonia.Media.IBrush PreviewTitleBrush =>
        new global::Avalonia.Media.SolidColorBrush(OverlayLook.Parse(OverlayTitleColor, Themes.ThemeManager.OverlayMuted(Themes.ThemeManager.Current)));
    public global::Avalonia.Media.IBrush PreviewGiftBrush =>
        new global::Avalonia.Media.SolidColorBrush(OverlayLook.Parse(OverlayGiftColor, Themes.ThemeManager.OverlayText(Themes.ThemeManager.Current)));
    public global::Avalonia.Media.IBrush PreviewCardBrush => OverlayShowBackground
        ? new global::Avalonia.Media.SolidColorBrush(OverlayLook.Parse(_shell.Prefs.OverlayBackgroundColor, Themes.ThemeManager.OverlayCard(Themes.ThemeManager.Current)))
        {
            Opacity = Math.Clamp(OverlayBackgroundOpacity, 0.15, 1),
        }
        : global::Avalonia.Media.Brushes.Transparent;

    public bool TwitchHasAccount => !string.IsNullOrWhiteSpace(TwitchLogin);
    public bool CanTwitchConnect => CanEditSetup && !TwitchBusy;
    public bool CaptureNeedsTikTok => SelectedMode == BridgeRunMode.Capture;

    public string ModeHint => SelectedMode switch
    {
        BridgeRunMode.Capture => "Anota regalos de TikTok. No toca la partida.",
        BridgeRunMode.Sdk => "Sin canales. Efectos desde HiveShock.",
        _ => "Los canales activos mandan efectos al juego.",
    };

    public string ChannelSummary
    {
        get
        {
            var parts = new List<string>();
            if (TikTokEnabled && !string.IsNullOrWhiteSpace(Channel))
            {
                parts.Add($"TikTok @{Channel.Trim().TrimStart('@')}");
            }
            else if (TikTokEnabled)
            {
                parts.Add("TikTok sin usuario");
            }

            if (TwitchHasAccount)
            {
                parts.Add($"Twitch @{TwitchLogin}");
            }
            else if (TwitchEnabled)
            {
                parts.Add("Twitch sin cuenta");
            }

            return parts.Count == 0
                ? "Ningún canal listo. Entra en TikTok o Twitch."
                : string.Join(" · ", parts);
        }
    }

    public string SessionHint => _shell.StatusTone switch
    {
        "connecting" => "Conectando… un momento.",
        "live" => string.IsNullOrWhiteSpace(_shell.Runtime.Ports.StatusSummary())
            ? "En vivo: lo que pasa en el chat llega al juego."
            : _shell.Runtime.Ports.StatusSummary() + ".",
        "ok" => $"{_shell.StatusText}.",
        _ => GameStatus,
    };

    public void RefreshProfiles()
    {
        Profiles = _shell.Runtime.ListProfiles()
            .Select(p => new ProfileCardViewModel(p, p.Id == _shell.Runtime.Profile.Id))
            .ToList();
        OnPropertyChanged(nameof(Profiles));
        var info = _shell.Runtime.Profile.Info;
        ProfileHint = string.IsNullOrWhiteSpace(info.Description) ? info.HelpNotes : info.Description;
        GameStatus = $"Listo: {_shell.Runtime.Profile.ShortDisplayName}.";
        RefreshCapabilities();
    }

    public void RefreshCapabilities()
    {
        var info = _shell.Runtime.Profile.Info;
        SupportsDeath = info.SupportsDeathEvents;
        CanRescue = info.SupportsRescue;
        CanDeleteSave = info.SupportsDeleteSave;
        HasGameTools = SupportsDeath || CanRescue || CanDeleteSave;
        RescueLabel = info.Label("rescue", "Rescatar");
        var delete = info.Label("delete_save", "Borrar partida");
        var cut = delete.IndexOf('(');
        DeleteSaveLabel = cut > 0 ? delete[..cut].Trim() : delete;
    }

    public void RefreshStats()
    {
        MappingSummary =
            $"{_shell.Runtime.Gifts.Snapshot.Groups.Count} regalos TikTok";
    }

    public void RefreshCounter()
    {
        var c = _shell.Runtime.DeathCounter;
        var title = _shell.Prefs.ResolveOverlayTitle();
        CounterDisplay = $"{title}  {c.Value}";
        _shell.Overlays.RefreshCounter(c, title, _shell.Prefs);
        NotifyPreview();
    }

    private bool IsDefaultCounterTitle()
    {
        var t = CounterTitleText.Trim();
        return string.IsNullOrWhiteSpace(t) ||
               t.Equals("Vidas", StringComparison.OrdinalIgnoreCase) ||
               t.Equals("Muertes", StringComparison.OrdinalIgnoreCase);
    }

    private void PersistLook()
    {
        if (_loading)
        {
            return;
        }

        _shell.Prefs.OverlayTitle = CounterTitleText.Trim();
        _shell.Prefs.GiftsOverlayTitle = string.IsNullOrWhiteSpace(GiftsTitleText) ? "Regalos" : GiftsTitleText.Trim();
        _shell.Prefs.ShowGiftsOverlayTitle = ShowGiftsTitle;
        _shell.Prefs.OverlayShowBackground = OverlayShowBackground;
        _shell.Prefs.OverlayBackgroundOpacity = OverlayBackgroundOpacity;
        _shell.Prefs.OverlayNumberSize = OverlayNumberSize;
        _shell.Prefs.OverlayTitleSize = OverlayTitleSize;
        _shell.Prefs.OverlayGiftNameSize = OverlayGiftNameSize;
        _shell.Prefs.OverlayNumberColor = OverlayNumberColor ?? "";
        _shell.Prefs.OverlayTitleColor = OverlayTitleColor ?? "";
        _shell.Prefs.OverlayGiftColor = OverlayGiftColor ?? "";
        _shell.Prefs.OverlayAlign = OverlayAlignCenter ? "center" : "left";
        _shell.Prefs.Save();
        _shell.Overlays.RefreshLook(_shell.Prefs, _shell.Runtime.DeathCounter, _shell.Gifts.Models);
        OnPropertyChanged(nameof(OverlayTitleIsLives));
        OnPropertyChanged(nameof(OverlayTitleIsDeaths));
        OnPropertyChanged(nameof(OverlayAlignLeft));
        NotifyPreview();
    }

    private void NotifyPreview()
    {
        OnPropertyChanged(nameof(IsNumberColorTheme));
        OnPropertyChanged(nameof(IsNumberColorGold));
        OnPropertyChanged(nameof(IsNumberColorBlue));
        OnPropertyChanged(nameof(IsNumberColorWhite));
        OnPropertyChanged(nameof(IsNumberColorBlack));
        OnPropertyChanged(nameof(IsTitleColorTheme));
        OnPropertyChanged(nameof(IsTitleColorGold));
        OnPropertyChanged(nameof(IsTitleColorWhite));
        OnPropertyChanged(nameof(IsTitleColorBlack));
        OnPropertyChanged(nameof(IsGiftColorTheme));
        OnPropertyChanged(nameof(IsGiftColorGold));
        OnPropertyChanged(nameof(IsGiftColorWhite));
        OnPropertyChanged(nameof(IsGiftColorBlack));
        OnPropertyChanged(nameof(PreviewNumberBrush));
        OnPropertyChanged(nameof(PreviewTitleBrush));
        OnPropertyChanged(nameof(PreviewGiftBrush));
        OnPropertyChanged(nameof(PreviewCardBrush));
        OnPropertyChanged(nameof(PreviewCounterValue));
        OnPropertyChanged(nameof(PreviewAlign));
    }

    partial void OnCounterTitleTextChanged(string value) => PersistLook();
    partial void OnGiftsTitleTextChanged(string value) => PersistLook();
    partial void OnShowGiftsTitleChanged(bool value) => PersistLook();
    partial void OnOverlayShowBackgroundChanged(bool value) => PersistLook();
    partial void OnOverlayBackgroundOpacityChanged(double value) => PersistLook();
    partial void OnOverlayNumberSizeChanged(double value) => PersistLook();
    partial void OnOverlayTitleSizeChanged(double value) => PersistLook();
    partial void OnOverlayGiftNameSizeChanged(double value) => PersistLook();
    partial void OnOverlayNumberColorChanged(string value) => PersistLook();
    partial void OnOverlayTitleColorChanged(string value) => PersistLook();
    partial void OnOverlayGiftColorChanged(string value) => PersistLook();
    partial void OnOverlayAlignCenterChanged(bool value) => PersistLook();

    [RelayCommand]
    private void SetNumberColor(string? hex) => OverlayNumberColor = NormalizeColor(hex);

    [RelayCommand]
    private void SetTitleColor(string? hex) => OverlayTitleColor = NormalizeColor(hex);

    [RelayCommand]
    private void SetGiftColor(string? hex) => OverlayGiftColor = NormalizeColor(hex);

    private static string NormalizeColor(string? hex) =>
        string.IsNullOrWhiteSpace(hex) || hex.Equals("theme", StringComparison.OrdinalIgnoreCase)
            ? ""
            : hex;

    [RelayCommand]
    private void UseTitleVidas() => CounterTitleText = "Vidas";

    [RelayCommand]
    private void UseTitleMuertes() => CounterTitleText = "Muertes";

    [RelayCommand]
    private void ResetOverlayLook()
    {
        _loading = true;
        _shell.Prefs.ResetOverlayLook();
        OverlayScale = _shell.Prefs.ResolveOverlayScale();
        CounterTitleText = LivesMode ? "Vidas" : "Muertes";
        GiftsTitleText = "Regalos";
        ShowGiftsTitle = true;
        OverlayShowBackground = true;
        OverlayBackgroundOpacity = 0.94;
        OverlayNumberSize = 72;
        OverlayTitleSize = 16;
        OverlayGiftNameSize = 14;
        OverlayNumberColor = "";
        OverlayTitleColor = "";
        OverlayGiftColor = "";
        OverlayAlignCenter = true;
        _loading = false;
        PersistLook();
        _shell.Overlays.RefreshOverlayScale(_shell.Prefs);
    }

    public void RefreshConnectLabel()
    {
        if (_shell.StatusTone == "connecting")
        {
            ConnectLabel = _shell.IsRunning ? "Cancelar" : "Conectando…";
        }
        else if (_shell.IsRunning)
        {
            ConnectLabel = SelectedMode switch
            {
                BridgeRunMode.Capture => "Desconectar · Anotando",
                BridgeRunMode.Sdk => "Desconectar · Pruebas",
                _ => "Desconectar",
            };
        }
        else
        {
            ConnectLabel = SelectedMode switch
            {
                BridgeRunMode.Capture => "Anotar regalos",
                BridgeRunMode.Sdk => "Probar sin live",
                _ => "Conectar",
            };
        }

        OnPropertyChanged(nameof(IsToneConnecting));
        OnPropertyChanged(nameof(IsToneLive));
        OnPropertyChanged(nameof(IsToneOk));
        OnPropertyChanged(nameof(SessionHint));
    }

    public void OnRunningChanged()
    {
        OnPropertyChanged(nameof(CanEditSetup));
        OnPropertyChanged(nameof(CanConnect));
        RefreshConnectLabel();
        RefreshPortCards();
    }

    public void RefreshPortCards()
    {
        var tiktok = _shell.Runtime.Ports.Get(LivePortIds.TikTok);
        var twitch = _shell.Runtime.Ports.Get(LivePortIds.Twitch);
        TikTokPortStatus = PortStatusLabel(tiktok.Status, tiktok.Message, TikTokEnabled);
        TwitchPortStatus = PortStatusLabel(twitch.Status, twitch.Message, TwitchEnabled);
        TikTokLive = TikTokEnabled && tiktok.Status == LivePortStatus.Live;
        TikTokConnecting = TikTokEnabled && tiktok.Status == LivePortStatus.Connecting;
        TikTokError = TikTokEnabled && tiktok.Status == LivePortStatus.Error;
        TwitchLogin = _shell.Runtime.Options.TwitchUserLogin;
        TwitchClientId = _shell.Runtime.Options.TwitchClientId;
        OnPropertyChanged(nameof(TwitchHasAccount));
        OnPropertyChanged(nameof(CanTwitchConnect));
        OnPropertyChanged(nameof(SessionHint));
        OnPropertyChanged(nameof(ChannelSummary));
    }

    private static string PortStatusLabel(LivePortStatus status, string message, bool enabled)
    {
        if (!enabled)
        {
            return "No se usa en este directo";
        }

        return status switch
        {
            LivePortStatus.Connecting => string.IsNullOrWhiteSpace(message) ? "Conectando…" : message,
            LivePortStatus.Live => "En vivo",
            LivePortStatus.Error => string.IsNullOrWhiteSpace(message) ? "Error" : message,
            LivePortStatus.Ended => "Live cerrado",
            _ => "Listo",
        };
    }

    partial void OnChannelChanged(string value) => OnPropertyChanged(nameof(ChannelSummary));
    partial void OnTikTokEnabledChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        _shell.Runtime.SetTikTokEnabled(value);
        RefreshPortCards();
    }

    partial void OnTwitchEnabledChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        _shell.Runtime.SetTwitchEnabled(value);
        RefreshPortCards();
    }

    private void LoadDeathPrefs()
    {
        _loading = true;
        LivesMode = _shell.Prefs.ResolveDeathMode() == DeathCounterMode.Lives;
        DeathStartText = _shell.Prefs.ResolveDeathStart().ToString();
        ShowCounterOnStream = _shell.Prefs.DeathOverlayEnabled;
        ShowGiftsOnStream = _shell.Prefs.GiftOverlayEnabled;
        ShowGoalsOnStream = _shell.Prefs.GoalOverlayEnabled;
        OverlayScale = _shell.Prefs.ResolveOverlayScale();
        var resolvedTitle = _shell.Prefs.ResolveOverlayTitle();
        CounterTitleText = resolvedTitle;
        GiftsTitleText = string.IsNullOrWhiteSpace(_shell.Prefs.GiftsOverlayTitle)
            ? "Regalos"
            : _shell.Prefs.GiftsOverlayTitle;
        ShowGiftsTitle = _shell.Prefs.ShowGiftsOverlayTitle;
        OverlayShowBackground = _shell.Prefs.OverlayShowBackground;
        OverlayBackgroundOpacity = OverlayLook.BackgroundOpacity(_shell.Prefs);
        OverlayNumberSize = OverlayLook.NumberSize(_shell.Prefs);
        OverlayTitleSize = OverlayLook.TitleSize(_shell.Prefs);
        OverlayGiftNameSize = OverlayLook.GiftNameSize(_shell.Prefs);
        OverlayNumberColor = _shell.Prefs.OverlayNumberColor ?? "";
        OverlayTitleColor = _shell.Prefs.OverlayTitleColor ?? "";
        OverlayGiftColor = _shell.Prefs.OverlayGiftColor ?? "";
        OverlayAlignCenter = OverlayLook.AlignCenter(_shell.Prefs);
        if (string.IsNullOrWhiteSpace(_shell.Prefs.OverlayTitle))
        {
            _shell.Prefs.OverlayTitle = LivesMode ? "vidas" : "muertes";
        }

        var mode = _shell.Prefs.ResolveDeathMode();
        var start = _shell.Prefs.ResolveDeathStart();
        _shell.Runtime.DeathCounter.Configure(mode, start, resetValue: false);
        _shell.Runtime.DeathCounter.Restore(_shell.Prefs.ResolveDeathValueToRestore());
        PersistDeath();
        RefreshCounter();
        _loading = false;
    }

    public void PersistDeath()
    {
        _shell.Prefs.DeathCounterValue = _shell.Runtime.DeathCounter.Value;
        _shell.Prefs.DeathCounterMode = _shell.Runtime.DeathCounter.Mode == DeathCounterMode.Lives ? "lives" : "count_up";
        _shell.Prefs.DeathCounterStart = _shell.Runtime.DeathCounter.StartingValue;
        _shell.Prefs.Save();
    }

    private void ApplyDeath(bool reset)
    {
        var mode = LivesMode ? DeathCounterMode.Lives : DeathCounterMode.CountUp;
        if (!int.TryParse(DeathStartText.Trim(), out var start))
        {
            start = mode == DeathCounterMode.Lives ? 500 : 0;
        }

        _shell.Prefs.DeathCounterMode = mode == DeathCounterMode.Lives ? "lives" : "count_up";
        _shell.Prefs.DeathCounterStart = start;
        _shell.Runtime.DeathCounter.Configure(mode, start, resetValue: reset);
        PersistDeath();
        RefreshCounter();
    }

    partial void OnSelectedModeChanged(BridgeRunMode value)
    {
        RefreshConnectLabel();
        OnPropertyChanged(nameof(IsModeLive));
        OnPropertyChanged(nameof(IsModeCapture));
        OnPropertyChanged(nameof(IsModeSdk));
        OnPropertyChanged(nameof(CaptureNeedsTikTok));
        OnPropertyChanged(nameof(ModeHint));
        OnPropertyChanged(nameof(ChannelSummary));
    }

    partial void OnLivesModeChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        if (value && (!int.TryParse(DeathStartText.Trim(), out var n) || n <= 0))
        {
            DeathStartText = "500";
        }

        if (!value && string.IsNullOrWhiteSpace(DeathStartText))
        {
            DeathStartText = "0";
        }

        OnPropertyChanged(nameof(CountUpMode));
        OnPropertyChanged(nameof(OverlayTitleIsLives));
        OnPropertyChanged(nameof(OverlayTitleIsDeaths));
        if (IsDefaultCounterTitle())
        {
            CounterTitleText = value ? "Vidas" : "Muertes";
        }

        ApplyDeath(reset: true);
    }

    partial void OnDeathStartTextChanged(string value)
    {
        if (_loading)
        {
            return;
        }

        ApplyDeath(reset: false);
    }

    partial void OnShowCounterOnStreamChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        _shell.Prefs.DeathOverlayEnabled = value;
        _shell.Prefs.Save();
        if (value)
        {
            _shell.Overlays.ShowCounter(_shell.Runtime.DeathCounter, _shell.Prefs.ResolveOverlayTitle(), _shell.Prefs);
        }
        else
        {
            _shell.Overlays.HideCounter(_shell.Prefs);
        }
    }

    partial void OnShowGiftsOnStreamChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        _shell.Prefs.GiftOverlayEnabled = value;
        _shell.Prefs.Save();
        if (value)
        {
            _shell.Overlays.ShowGifts(_shell.Gifts.Models, _shell.Prefs);
        }
        else
        {
            _shell.Overlays.HideGifts(_shell.Prefs);
        }
    }

    partial void OnShowGoalsOnStreamChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        _shell.Prefs.GoalOverlayEnabled = value;
        _shell.Prefs.Save();
        if (value)
        {
            _shell.Overlays.ShowGoals(_shell.Runtime.Goals.Snapshots(), _shell.Prefs);
        }
        else
        {
            _shell.Overlays.HideGoals(_shell.Prefs);
        }
    }

    partial void OnOverlayScaleChanged(double value)
    {
        if (_loading)
        {
            return;
        }

        var scale = Math.Clamp(value, 1.0, 3.0);
        if (Math.Abs(scale - value) > 0.0001)
        {
            OverlayScale = scale;
            return;
        }

        _shell.Prefs.OverlayScale = scale;
        _shell.Prefs.Save();
        _shell.Overlays.RefreshLook(_shell.Prefs, _shell.Runtime.DeathCounter, _shell.Gifts.Models);
    }

    [RelayCommand]
    private void SelectProfile(ProfileCardViewModel? card)
    {
        if (card == null)
        {
            return;
        }

        if (string.Equals(card.Id, _shell.Runtime.Profile.Id, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_shell.IsRunning)
        {
            _shell.Dialogs.Info("Cambia de juego", "Primero desconecta para elegir otro juego.");
            return;
        }

        try
        {
            _shell.Runtime.SwitchProfile(card.Id);
            _shell.OnProfileChanged();
            BridgeLog.Info($"Perfil UI: {_shell.Runtime.Profile.DisplayName}");
        }
        catch (Exception ex)
        {
            _shell.Dialogs.Error("Juego", ex.Message);
            RefreshProfiles();
        }
    }

    [RelayCommand]
    private void SaveChannel()
    {
        try
        {
            _shell.Runtime.SaveChannel(Channel);
            Channel = _shell.Runtime.Options.TikTokUniqueId;
            BridgeLog.Info($"Canal guardado: @{_shell.Runtime.Options.TikTokUniqueId}");
        }
        catch (Exception ex)
        {
            _shell.Dialogs.Warn("Usuario de TikTok", ex.Message);
        }
    }

    [RelayCommand]
    private Task ConnectAsync() => _shell.ToggleConnectionAsync();

    [RelayCommand]
    private void ResetCounter()
    {
        _shell.Runtime.ResetDeathCounter();
        PersistDeath();
        RefreshCounter();
    }

    [RelayCommand]
    private async Task RescueAsync()
    {
        try
        {
            await _shell.Runtime.RescueAsync().ConfigureAwait(true);
            BridgeLog.Info("Comando rescue enviado al juego.");
        }
        catch (Exception ex)
        {
            _shell.Dialogs.Error("Rescatar", ex.Message);
        }
    }

    [RelayCommand]
    private async Task DeleteSaveAsync()
    {
        if (!await _shell.Dialogs.ConfirmAsync(
                "Borrar partida",
                "Esto borra la partida del juego y vuelve al menú de archivos.\n\n¿Seguro?"))
        {
            return;
        }

        if (!await _shell.Dialogs.ConfirmTypedAsync(
                "Confirmar borrado",
                "Escribe BORRAR para confirmar que quieres borrar la partida:",
                "BORRAR"))
        {
            return;
        }

        try
        {
            await _shell.Runtime.DeleteSaveAsync().ConfigureAwait(true);
            _shell.Runtime.ResetDeathCounter();
            PersistDeath();
            RefreshCounter();
            BridgeLog.Info("Comando delete_save enviado al juego.");
        }
        catch (Exception ex)
        {
            _shell.Dialogs.Error("Borrar partida", ex.Message);
        }
    }

    [RelayCommand]
    private async Task ConnectTwitchAsync()
    {
        TwitchBusy = true;
        var cts = new CancellationTokenSource();
        var dlg = new TwitchDeviceDialog();
        var progress = new Progress<TwitchDeviceStart>(dlg.ShowStart);
        dlg.Closed += (_, _) => cts.Cancel();
        var owner = _shell.Dialogs.Owner;
        try
        {
            var login = _shell.Runtime.LoginTwitchAsync(progress, cts.Token);
            var shown = owner != null
                ? dlg.ShowDialog<bool?>(owner)
                : ShowLoose(dlg);
            try
            {
                await login.ConfigureAwait(true);
                dlg.Close(true);
            }
            catch (OperationCanceledException)
            {
                // cancel
            }

            await shown.ConfigureAwait(true);
            RefreshPortCards();
        }
        catch (Exception ex)
        {
            dlg.Close(false);
            _shell.Dialogs.Error("Twitch", ex.Message);
        }
        finally
        {
            TwitchBusy = false;
            OnPropertyChanged(nameof(CanTwitchConnect));
        }
    }

    partial void OnTwitchBusyChanged(bool value) => OnPropertyChanged(nameof(CanTwitchConnect));

    private static Task ShowLoose(global::Avalonia.Controls.Window dlg)
    {
        var tcs = new TaskCompletionSource<bool?>();
        dlg.Closed += (_, _) => tcs.TrySetResult(false);
        dlg.Show();
        return tcs.Task;
    }

    [RelayCommand]
    private void DisconnectTwitch()
    {
        _shell.Runtime.LogoutTwitch();
        RefreshPortCards();
    }

    [RelayCommand]
    private void SaveTwitchClientId()
    {
        if (string.IsNullOrWhiteSpace(TwitchClientId))
        {
            _shell.Dialogs.Warn("Twitch", "El Client ID no puede estar vacío.");
            return;
        }

        _shell.Runtime.SaveTwitchClientId(TwitchClientId);
        BridgeLog.Info("Twitch Client ID guardado.");
    }

    public bool EnsureChannelSaved()
    {
        if (!string.IsNullOrWhiteSpace(Channel) &&
            !string.Equals(Channel.Trim(), _shell.Runtime.Options.TikTokUniqueId, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                _shell.Runtime.SaveChannel(Channel);
                Channel = _shell.Runtime.Options.TikTokUniqueId;
            }
            catch (Exception ex)
            {
                _shell.Dialogs.Warn("TikTok", ex.Message);
                return false;
            }
        }

        _shell.Runtime.SetTikTokEnabled(TikTokEnabled);
        _shell.Runtime.SetTwitchEnabled(TwitchEnabled);

        if (SelectedMode is BridgeRunMode.Capture)
        {
            if (!_shell.Runtime.Options.TikTokReady)
            {
                _shell.Dialogs.Warn("TikTok", "Anotar regalos necesita este canal: marca Usar en este directo y guarda tu usuario (sin @).");
                return false;
            }
        }
        else if (SelectedMode is BridgeRunMode.Live)
        {
            if (!_shell.Runtime.Options.HasAnyLivePort)
            {
                _shell.Dialogs.Warn("Canales", "Activa TikTok (con usuario) o Twitch (con cuenta) para conectar en vivo.");
                return false;
            }
        }

        _shell.Runtime.SetDryRun(Simulate);
        return true;
    }
}
