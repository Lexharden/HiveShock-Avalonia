using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HiveShock.Avalonia.Services;
using HiveShock.Hosting;

namespace HiveShock.Avalonia.ViewModels;

/// <summary>Inicio · contador de muertes/vidas y overlays de ventana (qué se muestra y con qué aspecto).</summary>
public sealed partial class StudioViewModel
{
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
}
