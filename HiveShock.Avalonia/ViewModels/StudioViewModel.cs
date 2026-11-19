using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
        // Arrastrar una tarjeta de juego guarda el nuevo orden (no al rellenar la lista).
        Profiles.CollectionChanged += (_, e) =>
        {
            if (!_loadingProfiles && e.Action == NotifyCollectionChangedAction.Move)
            {
                _shell.Prefs.ProfileOrder = Profiles.Select(p => p.Id).ToList();
                _shell.Prefs.Save();
            }
        };
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
        LoadWebPrefs();
        RefreshStats();
        RefreshPortCards();
        shell.Runtime.Ports.Changed += () =>
            global::Avalonia.Threading.Dispatcher.UIThread.Post(RefreshPortCards);
    }

    private bool _loadingProfiles;

    /// <summary>Tarjetas de juego, en el orden que eligió el usuario arrastrándolas (se guarda en preferencias).</summary>
    public ObservableCollection<ProfileCardViewModel> Profiles { get; } = [];

    /// <summary>El perfil activo es uno propio: se puede borrar.</summary>
    public bool ActiveIsCustom => _shell.Runtime.Profile.IsCustom;

    [ObservableProperty] private string _channel = "";
    [ObservableProperty] private BridgeRunMode _selectedMode = BridgeRunMode.Live;
    [ObservableProperty] private bool _simulate;
    [ObservableProperty] private string _gameStatus = "";
    [ObservableProperty] private string _mappingSummary = "";
    [ObservableProperty] private bool _supportsDeath;
    [ObservableProperty] private bool _canRescue;
    [ObservableProperty] private bool _canDeleteSave;
    [ObservableProperty] private bool _hasGameTools;
    [ObservableProperty] private string _rescueLabel = "Rescatar";
    [ObservableProperty] private string _deleteSaveLabel = "Borrar partida";
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

    public bool TwitchHasAccount => !string.IsNullOrWhiteSpace(TwitchLogin);
    public bool CanTwitchConnect => CanEditSetup && !TwitchBusy;
    public bool CaptureNeedsTikTok => SelectedMode == BridgeRunMode.Capture;

    public string ModeHint => SelectedMode switch
    {
        BridgeRunMode.Capture => "Recibe los regalos de TikTok y los añade al catálogo. No envía efectos al juego.",
        BridgeRunMode.Sdk => "Sin TikTok ni Twitch: solo pruebas desde HiveShock.",
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
            ? "En vivo: lo que pasa en tu directo llega al juego."
            : _shell.Runtime.Ports.StatusSummary() + ".",
        "ok" => $"{_shell.StatusText}.",
        _ => GameStatus,
    };

    public void RefreshProfiles()
    {
        _loadingProfiles = true;
        try
        {
            var order = _shell.Prefs.ProfileOrder;
            Profiles.Clear();
            foreach (var card in _shell.Runtime.ListProfiles()
                         .Select((p, i) => (Card: new ProfileCardViewModel(p, p.Id == _shell.Runtime.Profile.Id), Index: i))
                         .OrderBy(x => order.IndexOf(x.Card.Id) is var at and >= 0 ? at : order.Count + x.Index)
                         .Select(x => x.Card))
            {
                Profiles.Add(card);
            }
        }
        finally
        {
            _loadingProfiles = false;
        }

        OnPropertyChanged(nameof(ActiveIsCustom));
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
                BridgeRunMode.Sdk => "Probar sin conexión",
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
            LivePortStatus.Ended => "Directo terminado",
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

    [RelayCommand]
    private async Task NewProfileAsync()
    {
        if (_shell.IsRunning)
        {
            _shell.Dialogs.Info("Nuevo perfil", "Primero desconecta para crear o cambiar de perfil.");
            return;
        }

        var request = await _shell.Dialogs.NewProfileAsync(_shell.Runtime.Profile.ShortDisplayName);
        if (request is not { } req)
        {
            return;
        }

        // Lo pendiente se guarda en el perfil de origen antes de crear (la copia parte de lo guardado) y de cambiar.
        if (!_shell.FlushPendingEdits("Nuevo perfil"))
        {
            return;
        }

        try
        {
            var created = _shell.Runtime.CreateProfile(req.Name, req.Template);
            _shell.Runtime.SwitchProfile(created.Id);
            _shell.OnProfileChanged();
            BridgeLog.Info($"Perfil propio creado: {created.DisplayName}");
        }
        catch (Exception ex)
        {
            _shell.Dialogs.Error("Nuevo perfil", ex.Message);
            RefreshProfiles();
        }
    }

    [RelayCommand]
    private async Task DeleteProfileAsync()
    {
        var profile = _shell.Runtime.Profile;
        if (!profile.IsCustom)
        {
            return;
        }

        if (_shell.IsRunning)
        {
            _shell.Dialogs.Info("Borrar perfil", "Primero desconecta para borrar un perfil.");
            return;
        }

        if (!await _shell.Dialogs.ConfirmAsync(
                "Borrar perfil",
                $"¿Borrar el perfil «{profile.DisplayName}»? Sus comandos y regalos dejarán de estar disponibles. " +
                "Las macros de teclas no se tocan (son de todos los perfiles)."))
        {
            return;
        }

        if (!_shell.FlushPendingEdits("Borrar perfil"))
        {
            return;
        }

        try
        {
            _shell.Runtime.DeleteProfile(profile.Id);
            _shell.OnProfileChanged();
        }
        catch (Exception ex)
        {
            _shell.Dialogs.Error("Borrar perfil", ex.Message);
            RefreshProfiles();
        }
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

        // Lo pendiente se guarda en el perfil de origen ANTES de cambiar; si no, se escribiría en el nuevo.
        if (!_shell.FlushPendingEdits("Juego"))
        {
            RefreshProfiles();
            return;
        }

        try
        {
            _shell.Runtime.SwitchProfile(card.Id);
            _shell.OnProfileChanged();
            BridgeLog.Info($"Perfil activo: {_shell.Runtime.Profile.DisplayName}");
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
            BridgeLog.Info("Rescate enviado al juego.");
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
            BridgeLog.Info("Orden de borrar la partida enviada al juego.");
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
            // Con la cuenta (o el permiso) nuevo ya se pueden leer las recompensas de puntos del canal.
            if (TwitchHasAccount)
            {
                await _shell.Events.LoadChannelRewardsAsync().ConfigureAwait(true);
            }
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
        _shell.Events.ForgetChannelRewards();
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
                _shell.Dialogs.Warn("TikTok", "Anotar regalos necesita este canal: activa «Activo en este directo» en TikTok y guarda tu usuario (sin @).");
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
