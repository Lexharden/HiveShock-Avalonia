using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HiveShock.Avalonia.Services;
using HiveShock.Zeldathon;

namespace HiveShock.Avalonia.ViewModels;

/// <summary>Página Zeldatón: conexión con el servidor de la carrera y ajustes del cronómetro en pantalla.</summary>
public sealed partial class ZeldathonViewModel : ViewModelBase
{
    /// <summary>Solo estas propiedades son ajustes del usuario (el resto es estado en pantalla y no dispara guardado).</summary>
    private static readonly HashSet<string> Editable =
    [
        nameof(ServerUrl), nameof(Token), nameof(AutoConnect), nameof(TimerLabel), nameof(TimerIsLocal),
        nameof(TimerFormatMs), nameof(TimerSize), nameof(ShowStatus), nameof(ShowReset), nameof(ShowBar),
        nameof(WarnMinutes), nameof(CriticalMinutes), nameof(NormalColor), nameof(WarnColor),
        nameof(CriticalColor), nameof(LocalCountdown), nameof(LocalStartMinutes),
        nameof(ShowDelta), nameof(DeltaAddColor), nameof(DeltaRemoveColor),
        nameof(DonationsEnabled), nameof(TikTokEnabled), nameof(TikTokRemove), nameof(TikTokUnits),
        nameof(TikTokSeconds), nameof(TikTokMin), nameof(TikTokMax), nameof(TwitchEnabled), nameof(TwitchRemove),
        nameof(TwitchUnits), nameof(TwitchSeconds), nameof(TwitchMin), nameof(TwitchMax),
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
        AutoSaver = new AutoSaver(SaveStatus, _ => SaveAll(), "Ajustes de Zeldatón");
        AutoSaver.Track(this, name => !Editable.Contains(name ?? ""));

        LoadFromSettings();
        _service.StateChanged += _ => Dispatcher.UIThread.Post(RefreshStatus);
        _service.Client.MessageRejected += (_, _, _) => Dispatcher.UIThread.Post(RefreshStatus);
        _service.Notice += message => Dispatcher.UIThread.Post(() =>
        {
            LastNotice = message;
            _shell.Dialogs.Warn("Zeldatón", message);
        });
        _service.Donations.Changed += () => Dispatcher.UIThread.Post(RefreshDonations);
        _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _tick.Tick += (_, _) => RefreshLive();
        _tick.Start();
        RefreshStatus();
        RefreshDonations();
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
    [ObservableProperty] private bool _showDelta = true;
    [ObservableProperty] private string _deltaAddColor = "#5BD68A";
    [ObservableProperty] private string _deltaRemoveColor = "#E07A7A";
    [ObservableProperty] private bool _localCountdown;
    [ObservableProperty] private int _localStartMinutes = 240;
    [ObservableProperty] private string _localText = "00:00:00";
    [ObservableProperty] private string _localToggleLabel = "Iniciar";

    // ---- tiempo por donaciones ----
    [ObservableProperty] private bool _donationsEnabled;
    [ObservableProperty] private bool _tikTokEnabled = true;
    [ObservableProperty] private bool _tikTokRemove;
    [ObservableProperty] private int _tikTokUnits = 1;
    [ObservableProperty] private int _tikTokSeconds = 1;
    [ObservableProperty] private int _tikTokMin = 1;
    [ObservableProperty] private int _tikTokMax;
    [ObservableProperty] private bool _twitchEnabled = true;
    [ObservableProperty] private bool _twitchRemove;
    [ObservableProperty] private int _twitchUnits = 1;
    [ObservableProperty] private int _twitchSeconds = 1;
    [ObservableProperty] private int _twitchMin = 1;
    [ObservableProperty] private int _twitchMax;
    [ObservableProperty] private string _donationPolicyText = "";
    [ObservableProperty] private string _donationPolicyWarning = "";
    [ObservableProperty] private string _tikTokExample = "";
    [ObservableProperty] private string _twitchExample = "";
    [ObservableProperty] private string _donationStatusText = "";

    /// <summary>Últimas donaciones y qué hizo el servidor con cada una.</summary>
    public ObservableCollection<DonationRowView> DonationRecent { get; } = [];

    public bool TikTokAdd
    {
        get => !TikTokRemove;
        set => TikTokRemove = !value;
    }

    public bool TwitchAdd
    {
        get => !TwitchRemove;
        set => TwitchRemove = !value;
    }

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
        ShowDelta = t.ShowDelta;
        DeltaAddColor = t.DeltaAddColor;
        DeltaRemoveColor = t.DeltaRemoveColor;
        LocalCountdown = t.LocalCountdown;
        LocalStartMinutes = t.LocalStartMinutes;
        var d = s.Donations ?? new DonationTimeSettings();
        DonationsEnabled = d.Enabled;
        (TikTokEnabled, TikTokRemove, TikTokUnits, TikTokSeconds, TikTokMin, TikTokMax) = Read(d.TikTok);
        (TwitchEnabled, TwitchRemove, TwitchUnits, TwitchSeconds, TwitchMin, TwitchMax) = Read(d.Twitch);
        _loading = false;
    }

    private static (bool, bool, int, int, int, int) Read(DonationTimeRule r) =>
        (r.Enabled, r.Direction == DonationTimeDirection.Remove, Math.Max(1, r.Units), Math.Max(1, r.Seconds),
            Math.Max(1, r.MinUnits), Math.Max(0, r.MaxSecondsPerDonation));

    private static DonationTimeRule Rule(bool enabled, bool remove, int units, int seconds, int min, int max) => new()
    {
        Enabled = enabled,
        Direction = remove ? DonationTimeDirection.Remove : DonationTimeDirection.Add,
        Units = Math.Clamp(units, 1, 1_000_000),
        Seconds = Math.Clamp(seconds, 1, 86_400),
        MinUnits = Math.Clamp(min, 1, 1_000_000),
        MaxSecondsPerDonation = Math.Clamp(max, 0, 172_800),
    };

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
        t.ShowDelta = ShowDelta;
        t.DeltaAddColor = NormalizeColor(DeltaAddColor);
        t.DeltaRemoveColor = NormalizeColor(DeltaRemoveColor);
        t.LocalCountdown = LocalCountdown;
        t.LocalStartMinutes = Math.Max(1, LocalStartMinutes);
        s.Donations = new DonationTimeSettings
        {
            Enabled = DonationsEnabled,
            TikTok = Rule(TikTokEnabled, TikTokRemove, TikTokUnits, TikTokSeconds, TikTokMin, TikTokMax),
            Twitch = Rule(TwitchEnabled, TwitchRemove, TwitchUnits, TwitchSeconds, TwitchMin, TwitchMax),
        };
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

        if (e.PropertyName == nameof(TikTokRemove))
        {
            OnPropertyChanged(nameof(TikTokAdd));
        }

        if (e.PropertyName == nameof(TwitchRemove))
        {
            OnPropertyChanged(nameof(TwitchAdd));
        }

        RefreshDonations();
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

    /// <summary>Muestra la animación sin tocar el reloj de verdad (los regalos de prueba no cambian el tiempo).</summary>
    [RelayCommand]
    private void PreviewDeltaAdd() => _shell.TimerSource.Deltas.Push(60, limited: false);

    [RelayCommand]
    private void PreviewDeltaRemove() => _shell.TimerSource.Deltas.Push(-60, limited: false);

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

    /// <summary>Límites del organizador, ejemplos con la tarifa elegida, totales y últimas donaciones.</summary>
    private void RefreshDonations()
    {
        var reporter = _service.Donations;
        var policy = reporter.Policy;
        var settings = _service.Settings.Donations ?? new DonationTimeSettings();

        DonationPolicyText = policy switch
        {
            null => "Los límites del organizador se leen al conectar con el servidor.",
            { Enabled: false } => "El organizador desactivó el tiempo por donaciones: no cambiarán el tiempo de nadie.",
            _ => $"El organizador permite que las donaciones {(policy.AllowAdd && policy.AllowRemove ? "sumen y resten" : policy.AllowAdd ? "solo sumen" : policy.AllowRemove ? "solo resten" : "no cambien")} tiempo · " +
                 $"hasta {DonationTimeCalculator.Format(policy.MaxSecondsPerDonation)} por donación · al día hasta " +
                 $"+{DonationTimeCalculator.Format(policy.MaxAddedSecondsPerDay)} / −{DonationTimeCalculator.Format(policy.MaxRemovedSecondsPerDay)}.",
        };

        var blocked = new List<string>();
        if (policy != null && settings.Enabled)
        {
            if (settings.TikTok.Enabled && !policy.Allows(settings.TikTok.Direction))
            {
                blocked.Add("TikTok");
            }

            if (settings.Twitch.Enabled && !policy.Allows(settings.Twitch.Direction))
            {
                blocked.Add("Twitch");
            }
        }

        DonationPolicyWarning = blocked.Count == 0
            ? ""
            : $"Con lo que eligiste para {string.Join(" y ", blocked)} el organizador no aplica nada: cambia «suman / restan».";

        TikTokExample = Examples(settings.TikTok, policy, [(1, "1 diamante"), (100, "100"), (29_999, "29 999 (León)")]);
        TwitchExample = Examples(settings.Twitch, policy, [(100, "100 bits"), (500, "500"), (5_000, "5 000")]);

        var pending = reporter.PendingCount;
        DonationStatusText = !settings.Enabled
            ? "Desactivado: las donaciones no cambian tu tiempo."
            : !_service.Settings.HasCredentials
                ? "Falta conectar con el servidor de la carrera (arriba)."
                : $"En esta sesión: +{DonationTimeCalculator.Format(reporter.AddedSeconds)} / −{DonationTimeCalculator.Format(reporter.RemovedSeconds)} confirmados" +
                  (pending > 0 ? $" · {pending} por confirmar (se reenvían solos)" : "");

        DonationRecent.Clear();
        foreach (var entry in reporter.Recent.Take(12))
        {
            DonationRecent.Add(DonationRowView.From(entry));
        }
    }

    private static string Examples(DonationTimeRule rule, DonationTimePolicy? policy, (long Amount, string Label)[] samples)
    {
        if (!rule.Enabled)
        {
            return "No cambian el tiempo.";
        }

        var sign = rule.Direction == DonationTimeDirection.Add ? 1 : -1;
        return "Ejemplos: " + string.Join(" · ", samples.Select(x =>
        {
            var seconds = DonationTimeCalculator.Preview(rule, x.Amount);
            var capped = policy is { MaxSecondsPerDonation: > 0 } && seconds > policy.MaxSecondsPerDonation
                ? $" (tope del organizador: {DonationTimeCalculator.Format(policy.MaxSecondsPerDonation)})"
                : "";
            return seconds == 0 ? $"{x.Label} → nada" : $"{x.Label} → {DonationTimeReporter.Signed(sign * seconds)}{capped}";
        }));
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

/// <summary>Una línea de la lista de donaciones de la página.</summary>
public sealed record DonationRowView(string Text, string Result, bool IsBad)
{
    public static DonationRowView From(DonationTimeEntry e)
    {
        var who = string.IsNullOrWhiteSpace(e.Viewer) ? "" : $" · {e.Viewer}";
        var platform = e.Platform == DonationPlatform.TikTok ? "TikTok" : "Twitch";
        var time = e.AppliedSeconds is { } applied
            ? DonationTimeReporter.Signed(applied)
            : e.RequestedSeconds != 0 ? DonationTimeReporter.Signed(e.RequestedSeconds) : "";
        var bad = e.Status.StartsWith("Rechazado", StringComparison.Ordinal) ||
                  e.Status.StartsWith("No enviado", StringComparison.Ordinal);
        return new DonationRowView(
            $"{e.AtUtc.ToLocalTime():HH:mm:ss} · {platform} · {e.What}{who}",
            time.Length > 0 ? $"{time} · {e.Status}" : e.Status,
            bad);
    }
}
