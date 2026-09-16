using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HiveShock.Avalonia.Services;
using HiveShock.Avalonia.Themes;
using HiveShock.Hosting;
using HiveShock.Logging;

namespace HiveShock.Avalonia.ViewModels;

public sealed partial class MainViewModel : ViewModelBase, IAsyncDisposable
{
    private const int MaxLogLines = 500;
    private const int MaxActivity = 40;

    private readonly List<string> _logLines = [];
    private readonly List<string> _activityLines = [];
    private bool _closing;
    /// <summary>True cuando TikTok / puerto de pruebas ya respondió (no solo el host arrancó).</summary>
    private bool _sessionReady;

    public MainViewModel(DialogService dialogs, OverlayService overlays)
    {
        Dialogs = dialogs;
        Overlays = overlays;
        Runtime = BridgeRuntime.Create();
        Prefs = UiPreferences.Load();
        if (Application.Current != null)
        {
            ThemeManager.Apply(Application.Current, Prefs.ResolveTheme(), Prefs.FollowsSystem);
        }

        EffectChoices = [];
        ReloadEffectChoices();

        Studio = new StudioViewModel(this);
        Gifts = new GiftsViewModel(this);
        Events = new EventsViewModel(this);
        Catalog = new CatalogViewModel(this);
        Goals = new GoalsViewModel(this);
        TikTok = new TikTokViewModel(this);
        Twitch = new TwitchViewModel(this);
        Help = new HelpViewModel();
        About = new AboutViewModel();
        Events.LoadFromEditor(Gifts.Editor);
        Goals.LoadFromEditor(Gifts.Editor);

        CurrentPage = Studio;
        CurrentPageKey = "studio";
        DetailLogOpen = Prefs.DetailLogOpen;
        NavExpanded = Prefs.NavExpanded;
        SelectedThemeId = Prefs.Theme is "dark" or "light" ? Prefs.Theme : "system";

        Overlays.CounterClosedByUser += () =>
        {
            Studio.ShowCounterOnStream = false;
        };
        Overlays.GiftsClosedByUser += () =>
        {
            Studio.ShowGiftsOnStream = false;
        };
        Overlays.GoalsClosedByUser += () =>
        {
            Studio.ShowGoalsOnStream = false;
        };

        Runtime.DeathsChanged += (_, _) => Dispatch(() =>
        {
            Studio.PersistDeath();
            Studio.RefreshCounter();
        });
        Runtime.Goals.Changed += (_, _) => Dispatch(() =>
        {
            Overlays.RefreshGoals(Runtime.Goals.Snapshots(), Prefs);
        });
        Runtime.Overlay.GiftReceived += OnOverlayGift;
        Runtime.Ports.Changed += OnPortsChanged;
        BridgeLog.Logged += OnLogged;
        BridgeLog.Init();
        BridgeLog.Info($"UI lista · perfil {Runtime.Profile.DisplayName}");
        RefreshStatus();
    }

    public BridgeRuntime Runtime { get; }
    public UiPreferences Prefs { get; }
    public DialogService Dialogs { get; }
    public OverlayService Overlays { get; }

    public StudioViewModel Studio { get; }
    public GiftsViewModel Gifts { get; }
    public CatalogViewModel Catalog { get; }
    public EventsViewModel Events { get; }
    public GoalsViewModel Goals { get; }
    public TikTokViewModel TikTok { get; }
    public TwitchViewModel Twitch { get; }
    public HelpViewModel Help { get; }
    public AboutViewModel About { get; }

    public System.Collections.ObjectModel.ObservableCollection<EffectChoice> EffectChoices { get; }

    public IReadOnlyList<ThemeOption> ThemeOptions { get; } =
    [
        new("system", "Igual que el sistema"),
        new("dark", "Oscuro"),
        new("light", "Claro"),
    ];

    public ThemeOption? SelectedTheme
    {
        get => ThemeOptions.FirstOrDefault(t => t.Id == SelectedThemeId) ?? ThemeOptions[0];
        set
        {
            if (value != null && value.Id != SelectedThemeId)
            {
                SelectedThemeId = value.Id;
            }
        }
    }

    [ObservableProperty] private object? _currentPage;
    [ObservableProperty] private string _currentPageKey = "studio";
    [ObservableProperty] private string _statusText = "Listo";
    [ObservableProperty] private string _statusTone = "idle";
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _detailLogOpen;
    [ObservableProperty] private string _activityText = "Nada todavía. Cuando conectes, aquí verás lo que pasa.";
    [ObservableProperty] private string _detailLogText = "";
    [ObservableProperty] private string _selectedThemeId = "system";
    [ObservableProperty] private bool _navExpanded = true;

    public bool IsStudioNav => CurrentPageKey == "studio";
    public bool IsTikTokNav => CurrentPageKey is "tiktok" or "gifts" or "catalog";
    public bool IsTwitchNav => CurrentPageKey == "twitch";
    public bool IsHelpNav => CurrentPageKey == "help";
    public bool IsAboutNav => CurrentPageKey == "about";
    public bool CanInteract => !IsBusy;
    public bool StatusDotVisible => StatusTone is "connecting" or "ok" or "live";
    public bool IsToneIdle => StatusTone == "idle";
    public bool IsToneConnecting => StatusTone == "connecting";
    public bool IsToneOk => StatusTone == "ok";
    public bool IsToneLive => StatusTone == "live";
    public bool StatusOn => StatusTone is "connecting" or "ok" or "live";
    public bool NavCollapsed => !NavExpanded;
    public double SidebarWidth => NavExpanded ? 208 : 56;
    public string NavToggleTip => NavExpanded ? "Ocultar menú" : "Mostrar menú";

    public void Attach()
    {
        if (Prefs.DeathOverlayEnabled)
        {
            Overlays.ShowCounter(Runtime.DeathCounter, Prefs.ResolveOverlayTitle(), Prefs);
        }

        if (Prefs.GiftOverlayEnabled)
        {
            Overlays.ShowGifts(Gifts.Models, Prefs);
        }

        if (Prefs.GoalOverlayEnabled)
        {
            Overlays.ShowGoals(Runtime.Goals.Snapshots(), Prefs);
        }
    }

    public void ReloadEffectChoices()
    {
        EffectChoices.Clear();
        foreach (var (id, label) in Runtime.Effects.ListEntries())
        {
            EffectChoices.Add(new EffectChoice(id, label));
        }
    }

    public void OnProfileChanged()
    {
        ReloadEffectChoices();
        Studio.RefreshProfiles();
        Gifts.Load();
        Catalog.Load();
        Events.NotifyEffects();
        Goals.NotifyEffects();
        Studio.RefreshStats();
        Overlays.RefreshGifts(Gifts.Models);
        Overlays.RefreshGoals(Runtime.Goals.Snapshots(), Prefs);
    }

    public void RefreshStatus()
    {
        IsRunning = Runtime.IsRunning;
        if (!Runtime.IsRunning)
        {
            _sessionReady = false;
            StatusText = "Listo";
            StatusTone = "idle";
        }
        else if (!_sessionReady &&
                 Runtime.ActiveMode is BridgeRunMode.Live or BridgeRunMode.Capture)
        {
            StatusText = "Conectando…";
            StatusTone = "connecting";
        }
        else
        {
            (StatusText, StatusTone) = Runtime.ActiveMode switch
            {
                BridgeRunMode.Capture => ("Anotando", "ok"),
                BridgeRunMode.Sdk => ("En pruebas", "ok"),
                _ => (string.IsNullOrWhiteSpace(Runtime.Ports.StatusSummary())
                    ? "En vivo"
                    : Runtime.Ports.StatusSummary(), "live"),
            };
        }

        Studio.OnRunningChanged();
        Studio.RefreshStats();
    }

    partial void OnStatusToneChanged(string value)
    {
        OnPropertyChanged(nameof(StatusDotVisible));
        OnPropertyChanged(nameof(IsToneIdle));
        OnPropertyChanged(nameof(IsToneConnecting));
        OnPropertyChanged(nameof(IsToneOk));
        OnPropertyChanged(nameof(IsToneLive));
        OnPropertyChanged(nameof(StatusOn));
        Studio.RefreshConnectLabel();
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanInteract));
        Studio.OnRunningChanged();
    }

    partial void OnCurrentPageKeyChanged(string value)
    {
        OnPropertyChanged(nameof(IsStudioNav));
        OnPropertyChanged(nameof(IsTikTokNav));
        OnPropertyChanged(nameof(IsTwitchNav));
        OnPropertyChanged(nameof(IsHelpNav));
        OnPropertyChanged(nameof(IsAboutNav));
    }

    public async Task ToggleConnectionAsync()
    {
        if (Runtime.IsRunning)
        {
            IsBusy = true;
            StatusText = "Desconectando…";
            StatusTone = "connecting";
            Studio.RefreshConnectLabel();
            try
            {
                await Runtime.StopAsync().ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                // Desconectar cancela el host a propósito.
            }
            finally
            {
                IsBusy = false;
                RefreshStatus();
            }

            return;
        }

        if (!Studio.EnsureChannelSaved())
        {
            return;
        }

        IsBusy = true;
        _sessionReady = false;
        StatusText = "Conectando…";
        StatusTone = "connecting";
        Studio.RefreshConnectLabel();
        try
        {
            await Runtime.StartAsync(Studio.SelectedMode).ConfigureAwait(true);
            if (Studio.SelectedMode == BridgeRunMode.Sdk)
            {
                _sessionReady = true;
            }

            _ = WatchRuntimeAsync();
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch (Exception ex)
        {
            Dialogs.Error("Conectar", FriendlyStartError(ex.Message));
        }
        finally
        {
            IsBusy = false;
            RefreshStatus();
        }
    }

    private static string FriendlyStartError(string message)
    {
        if (message.Contains("canal", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("TikTok", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Twitch", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Activa al menos", StringComparison.OrdinalIgnoreCase))
        {
            return message;
        }

        return message;
    }

    private async Task WatchRuntimeAsync()
    {
        try
        {
            await Runtime.WaitUntilStoppedAsync().ConfigureAwait(true);
        }
        catch
        {
            // ignore
        }

        if (_closing)
        {
            return;
        }

        Dispatch(() =>
        {
            RefreshStatus();
            Catalog.Load();
        });
    }

    public void GoStudio()
    {
        CurrentPage = Studio;
        CurrentPageKey = "studio";
    }

    public void GoTikTok()
    {
        Gifts.Load();
        Catalog.Load();
        Events.LoadFromEditor(Gifts.Editor);
        Goals.LoadFromEditor(Gifts.Editor);
        CurrentPage = TikTok;
        CurrentPageKey = "tiktok";
    }

    public void GoTwitch()
    {
        Events.LoadFromEditor(Gifts.Editor);
        CurrentPage = Twitch;
        CurrentPageKey = "twitch";
    }

    public void GoGifts(bool reload = true)
    {
        GoTikTok();
    }

    public void GoCatalog()
    {
        GoTikTok();
    }

    public void GoEvents()
    {
        GoTikTok();
    }

    public void GoHelp()
    {
        CurrentPage = Help;
        CurrentPageKey = "help";
    }

    public void GoAbout()
    {
        CurrentPage = About;
        CurrentPageKey = "about";
    }

    [RelayCommand]
    private void NavStudio() => GoStudio();

    [RelayCommand]
    private void NavTikTok() => GoTikTok();

    [RelayCommand]
    private void NavTwitch() => GoTwitch();

    [RelayCommand]
    private void ToggleNav() => NavExpanded = !NavExpanded;

    [RelayCommand]
    private void NavHelp() => GoHelp();

    [RelayCommand]
    private void NavAbout() => GoAbout();

    [RelayCommand]
    private void ClearActivity()
    {
        _activityLines.Clear();
        _logLines.Clear();
        ActivityText = "";
        DetailLogText = "";
    }

    [RelayCommand]
    private void ClearLog() => ClearActivity();

    partial void OnNavExpandedChanged(bool value)
    {
        Prefs.NavExpanded = value;
        Prefs.Save();
        OnPropertyChanged(nameof(NavToggleTip));
        OnPropertyChanged(nameof(NavCollapsed));
        OnPropertyChanged(nameof(SidebarWidth));
    }

    partial void OnDetailLogOpenChanged(bool value)
    {
        Prefs.DetailLogOpen = value;
        Prefs.Save();
    }

    partial void OnSelectedThemeIdChanged(string value)
    {
        Prefs.Theme = value;
        Prefs.Save();
        OnPropertyChanged(nameof(SelectedTheme));
        if (Application.Current != null)
        {
            ThemeManager.Apply(Application.Current, Prefs.ResolveTheme(), Prefs.FollowsSystem);
        }
    }

    private void OnPortsChanged() => Dispatch(() =>
    {
        Studio.RefreshPortCards();
        if (Runtime.IsRunning)
        {
            if (Runtime.Ports.AnyLive())
            {
                _sessionReady = true;
            }

            RefreshStatus();
        }
    });

    private void OnOverlayGift(object? sender, OverlayGiftHitEventArgs e) =>
        Dispatch(() => Overlays.HighlightGift(e.GiftName, e.GiftId));

    private void OnLogged(LogEntry entry)
    {
        if (_closing)
        {
            return;
        }

        Dispatch(() =>
        {
            _logLines.Add(entry.Formatted);
            while (_logLines.Count > MaxLogLines)
            {
                _logLines.RemoveAt(0);
            }

            DetailLogText = string.Join(Environment.NewLine, _logLines);

            var friendly = ActivityCopy.ToViewerLine(entry);
            if (friendly != null)
            {
                _activityLines.Add($"{DateTime.Now:HH:mm}  {friendly}");
                while (_activityLines.Count > MaxActivity)
                {
                    _activityLines.RemoveAt(0);
                }

                ActivityText = string.Join(Environment.NewLine, _activityLines);
            }

            if (MarksSessionReady(entry.Message))
            {
                _sessionReady = true;
                RefreshStatus();
            }
        });
    }

    private static bool MarksSessionReady(string message) =>
        message.StartsWith("TikTok conectado", StringComparison.Ordinal) ||
        message.StartsWith("Twitch conectado", StringComparison.Ordinal) ||
        message.StartsWith("Pruebas escuchando", StringComparison.Ordinal) ||
        message.StartsWith("Captura de regalos", StringComparison.Ordinal) ||
        message.StartsWith("Modo pruebas:", StringComparison.Ordinal);

    private static void Dispatch(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }

    public async Task ShutdownAsync()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        Studio.PersistDeath();
        Overlays.CloseAll(Prefs);
        Runtime.Overlay.GiftReceived -= OnOverlayGift;
        Runtime.Ports.Changed -= OnPortsChanged;
        BridgeLog.Logged -= OnLogged;

        try
        {
            await Runtime.StopAsync().ConfigureAwait(true);
        }
        catch
        {
            // ignore
        }

        BridgeLog.Close();

        try
        {
            await Runtime.DisposeAsync().ConfigureAwait(true);
        }
        catch
        {
            // ignore
        }
    }

    public async ValueTask DisposeAsync() => await ShutdownAsync().ConfigureAwait(true);
}

public sealed record ThemeOption(string Id, string Label);
