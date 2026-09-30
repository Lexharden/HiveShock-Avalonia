using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HiveShock.Avalonia.Services;
using HiveShock.Zeldathon;

namespace HiveShock.Avalonia.ViewModels;

/// <summary>Página Zeldathon: conexión con el servidor de la carrera y ajustes del cronómetro en pantalla.</summary>
public sealed partial class ZeldathonViewModel : ViewModelBase
{
    /// <summary>Solo estas propiedades son ajustes del usuario (el resto es estado en pantalla y no dispara guardado).</summary>
    private static readonly HashSet<string> Editable =
    [
        nameof(ServerUrl), nameof(Token), nameof(AutoConnect), nameof(TimerLabel), nameof(TimerIsLocal),
        nameof(TimerFormatMs), nameof(TimerSize), nameof(ShowStatus), nameof(ShowReset), nameof(ShowBar),
        nameof(WarnMinutes), nameof(CriticalMinutes), nameof(NormalColor), nameof(WarnColor),
        nameof(CriticalColor), nameof(LocalCountdown), nameof(LocalStartMinutes),
    ];

    private readonly MainViewModel _shell;
    private readonly ZeldathonService _service;
    private readonly DispatcherTimer _tick;
    private bool _loading;

    public ZeldathonViewModel(MainViewModel shell)
    {
        _shell = shell;
        _service = shell.Runtime.Zeldathon;
        SaveStatus = new SaveStatusViewModel(shell.Prefs);
        AutoSaver = new AutoSaver(SaveStatus, _ => SaveAll(), "Ajustes de Zeldathon");
        AutoSaver.Track(this, name => !Editable.Contains(name ?? ""));

        LoadFromSettings();
        _service.StateChanged += _ => Dispatcher.UIThread.Post(RefreshStatus);
        _service.Client.MessageRejected += (_, _, _) => Dispatcher.UIThread.Post(RefreshStatus);
        _service.Notice += message => Dispatcher.UIThread.Post(() =>
        {
            LastNotice = message;
            _shell.Dialogs.Warn("Zeldathon", message);
        });
        _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _tick.Tick += (_, _) => RefreshLive();
        _tick.Start();
        RefreshStatus();
    }

    public SaveStatusViewModel SaveStatus { get; }
    public AutoSaver AutoSaver { get; }
    public StudioViewModel Studio => _shell.Studio;

    // ---- conexión ----
    [ObservableProperty] private string _serverUrl = "";
    [ObservableProperty] private string _token = "";
    [ObservableProperty] private bool _autoConnect = true;
    [ObservableProperty] private bool _showToken;
    [ObservableProperty] private string _connectionText = "Desconectado";
    [ObservableProperty] private bool _toneLive;
    [ObservableProperty] private bool _toneConnecting;
    [ObservableProperty] private bool _toneError;
    [ObservableProperty] private string _lastError = "";
    [ObservableProperty] private string _traffic = "";
    [ObservableProperty] private string _lastNotice = "";
    [ObservableProperty] private string _connectLabel = "Conectar";

    // ---- reloj oficial (vista en la página) ----
    [ObservableProperty] private string _clockText = "--:--:--";
    [ObservableProperty] private string _clockStatus = "";
    [ObservableProperty] private string _resetText = "";

    // ---- cronómetro en pantalla ----
    [ObservableProperty] private string _timerLabel = "Tiempo restante";
    [ObservableProperty] private bool _timerIsLocal;
    [ObservableProperty] private bool _timerFormatMs;
    [ObservableProperty] private double _timerSize = 60;
    [ObservableProperty] private bool _showStatus = true;
    [ObservableProperty] private bool _showReset;
    [ObservableProperty] private bool _showBar = true;
    [ObservableProperty] private int _warnMinutes = 30;
    [ObservableProperty] private int _criticalMinutes = 5;
    [ObservableProperty] private string _normalColor = "";
    [ObservableProperty] private string _warnColor = "#EBA00A";
    [ObservableProperty] private string _criticalColor = "#E07A7A";
    [ObservableProperty] private bool _localCountdown;
    [ObservableProperty] private int _localStartMinutes = 240;
    [ObservableProperty] private string _localText = "00:00:00";
    [ObservableProperty] private string _localToggleLabel = "Iniciar";

    public bool TimerIsOfficial
    {
        get => !TimerIsLocal;
        set => TimerIsLocal = !value;
    }

    public bool TimerFormatHms
    {
        get => !TimerFormatMs;
        set => TimerFormatMs = !value;
    }

    public bool IsConnected => _service.State == ZeldathonConnectionState.Connected;

    public bool IsRunning => _service.Client.IsRunning;

    private void LoadFromSettings()
    {
        using var _ = AutoSaver.Suppress();
        _loading = true;
        var s = _service.Settings;
        ServerUrl = s.ServerUrl;
        Token = s.Token;
        AutoConnect = s.AutoConnect;
        var t = _shell.Prefs.Timer;
        TimerLabel = t.Label;
        TimerIsLocal = t.IsLocal;
        TimerFormatMs = string.Equals(t.Format, "ms", StringComparison.OrdinalIgnoreCase);
        TimerSize = t.ResolveTimeSize();
        ShowStatus = t.ShowStatus;
        ShowReset = t.ShowReset;
        ShowBar = t.ShowBar;
        WarnMinutes = t.WarnMinutes;
        CriticalMinutes = t.CriticalMinutes;
        NormalColor = t.NormalColor;
        WarnColor = t.WarnColor;
        CriticalColor = t.CriticalColor;
        LocalCountdown = t.LocalCountdown;
        LocalStartMinutes = t.LocalStartMinutes;
        _loading = false;
    }

    /// <summary>Pasa lo editado a los ajustes vivos (el overlay lo refleja al instante) y lo escribe a disco.</summary>
    private void SaveAll()
    {
        PushToSettings();
        _service.Settings.Save();
        _shell.Prefs.Save();
    }

    private void PushToSettings()
    {
        var s = _service.Settings;
        s.ServerUrl = ServerUrl.Trim();
        s.Token = Token.Trim();
        s.AutoConnect = AutoConnect;
        var t = _shell.Prefs.Timer;
        t.Label = string.IsNullOrWhiteSpace(TimerLabel) ? "Tiempo restante" : TimerLabel.Trim();
        t.Mode = TimerIsLocal ? "local" : "official";
        t.Format = TimerFormatMs ? "ms" : "hms";
        t.TimeSize = TimerSize;
        t.ShowStatus = ShowStatus;
        t.ShowReset = ShowReset;
        t.ShowBar = ShowBar;
        t.WarnMinutes = Math.Max(0, WarnMinutes);
        t.CriticalMinutes = Math.Max(0, CriticalMinutes);
        t.NormalColor = NormalizeColor(NormalColor);
        t.WarnColor = NormalizeColor(WarnColor);
        t.CriticalColor = NormalizeColor(CriticalColor);
        t.LocalCountdown = LocalCountdown;
        t.LocalStartMinutes = Math.Max(1, LocalStartMinutes);
    }

    /// <summary>Cada cambio se ve al momento en el overlay (aunque el disco espere al guardado automático).</summary>
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_loading || e.PropertyName == null || !Editable.Contains(e.PropertyName))
        {
            return;
        }

        PushToSettings();
        _shell.Overlays.RefreshTimer(_shell.Prefs);
        if (e.PropertyName == nameof(TimerIsLocal))
        {
            OnPropertyChanged(nameof(TimerIsOfficial));
        }

        if (e.PropertyName == nameof(TimerFormatMs))
        {
            OnPropertyChanged(nameof(TimerFormatHms));
        }
    }

    [RelayCommand]
    private void ToggleConnection()
    {
        if (IsRunning)
        {
            _service.Disconnect();
            RefreshStatus();
            return;
        }

        AutoSaver.SaveNow();
        _service.Settings.Enabled = true;
        _service.Settings.Save();
        var error = _service.Connect();
        LastError = error ?? "";
        RefreshStatus();
    }

    [RelayCommand]
    private void ResetLook()
    {
        _shell.Prefs.Timer.ResetLook();
        LoadFromSettings();
        AutoSaver.NotifyChanged();
        _shell.Overlays.RefreshTimer(_shell.Prefs);
    }

    [RelayCommand]
    private void LocalToggle()
    {
        var sw = _shell.TimerSource.Local;
        if (sw.IsRunning)
        {
            sw.Pause();
            _shell.TimerSource.PersistLocal();
        }
        else
        {
            sw.Start();
        }

        RefreshLive();
    }

    [RelayCommand]
    private void LocalReset()
    {
        _shell.TimerSource.Local.Reset();
        _shell.TimerSource.PersistLocal();
        RefreshLive();
    }

    private void RefreshStatus()
    {
        var state = _service.State;
        var running = _service.Client.IsRunning;
        ConnectionText = state switch
        {
            ZeldathonConnectionState.Connecting => "Conectando…",
            ZeldathonConnectionState.Connected => _service.EventStatus switch
            {
                "live" => "Conectado · evento en vivo",
                "upcoming" => "Conectado · el evento aún no empieza",
                "paused" => "Conectado · evento en pausa",
                "finished" => "Conectado · evento terminado",
                _ => "Conectado",
            },
            ZeldathonConnectionState.Reconnecting => "Reconectando…",
            ZeldathonConnectionState.AuthFailed => "Token no válido",
            ZeldathonConnectionState.Replaced => "Otra conexión tomó tu lugar",
            _ => "Desconectado",
        };
        ToneLive = state == ZeldathonConnectionState.Connected;
        ToneConnecting = state is ZeldathonConnectionState.Connecting or ZeldathonConnectionState.Reconnecting;
        ToneError = state is ZeldathonConnectionState.AuthFailed or ZeldathonConnectionState.Replaced;
        ConnectLabel = running ? "Desconectar" : "Conectar";
        if (_service.Client.LastError.Length > 0)
        {
            LastError = _service.Client.LastError;
        }
        else if (state == ZeldathonConnectionState.Connected)
        {
            LastError = "";
        }

        var c = _service.Client;
        Traffic = c.Sent > 0 || c.Acked > 0 || c.Rejected > 0
            ? $"Enviados {c.Sent} · confirmados {c.Acked} · rechazados {c.Rejected}"
            : "";
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(IsRunning));
    }

    private void RefreshLive()
    {
        var shown = _shell.TimerSource.CurrentOfficial();
        ClockText = shown.TimeText;
        ClockStatus = shown.StatusText;
        ResetText = shown.ResetText;

        var sw = _shell.TimerSource.Local;
        LocalText = TimerDisplayBuilder.FormatTime(sw.ElapsedMs, "hms", roundUp: false);
        LocalToggleLabel = sw.IsRunning ? "Pausar" : "Iniciar";
    }

    private static string NormalizeColor(string? hex)
    {
        var text = (hex ?? "").Trim();
        if (text.Length == 0)
        {
            return "";
        }

        if (!text.StartsWith('#'))
        {
            text = "#" + text;
        }

        return global::Avalonia.Media.Color.TryParse(text, out _) ? text.ToUpperInvariant() : "";
    }
}
