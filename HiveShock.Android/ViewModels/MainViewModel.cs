using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HiveShock.Configuration;
using HiveShock.Hosting;
using HiveShock.Live;
using HiveShock.Logging;

namespace HiveShock.Android.ViewModels;

public sealed class ProfileItem
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public bool IsSelected { get; init; }
}

public sealed class EffectItem
{
    public required string Id { get; init; }
    public required string Display { get; init; }
}

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private const int MaxActivity = 40;
    private readonly List<string> _activity = [];
    private CancellationTokenSource? _twitchCts;
    private bool _busy;

    public MainViewModel()
    {
        Runtime = AndroidBridge.Shared;
        GameHost = Runtime.Options.GameHost;
        TikTokUser = Runtime.Options.TikTokUniqueId;
        TikTokEnabled = Runtime.Options.TikTokEnabled;
        TwitchEnabled = Runtime.Options.TwitchEnabled;
        RefreshProfiles();
        RefreshEffects();
        RefreshTwitchAccount();
        RefreshStatus();
        Runtime.Ports.Changed += OnPortsChanged;
        BridgeLog.Logged += OnLogged;
        BridgeLog.Init();
        BridgeLog.Info($"Android · {Runtime.Profile.DisplayName}");
        AppendActivity($"Perfil {Runtime.Profile.DisplayName}");
    }

    public BridgeRuntime Runtime { get; }

    public ObservableCollection<ProfileItem> Profiles { get; } = [];
    public ObservableCollection<EffectItem> Effects { get; } = [];

    [ObservableProperty] private string _gameHost = "";
    [ObservableProperty] private string _tikTokUser = "";
    [ObservableProperty] private bool _tikTokEnabled = true;
    [ObservableProperty] private bool _twitchEnabled = true;
    [ObservableProperty] private string _twitchAccount = "Sin cuenta";
    [ObservableProperty] private string _statusText = "Listo";
    [ObservableProperty] private string _connectLabel = "Conectar";
    [ObservableProperty] private bool _isLive;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _canEdit = true;
    [ObservableProperty] private bool _canConnect = true;
    [ObservableProperty] private string _activityText = "";
    [ObservableProperty] private EffectItem? _selectedEffect;
    [ObservableProperty] private bool _twitchCodeVisible;
    [ObservableProperty] private string _twitchDeviceCode = "";
    [ObservableProperty] private string _twitchDeviceHint = "";

    partial void OnTikTokEnabledChanged(bool value)
    {
        if (_busy)
        {
            return;
        }

        try
        {
            Runtime.SetTikTokEnabled(value);
        }
        catch (Exception ex)
        {
            AppendActivity(ex.Message);
        }
    }

    partial void OnTwitchEnabledChanged(bool value)
    {
        if (_busy)
        {
            return;
        }

        try
        {
            Runtime.SetTwitchEnabled(value);
        }
        catch (Exception ex)
        {
            AppendActivity(ex.Message);
        }
    }

    [RelayCommand]
    private void SelectProfile(ProfileItem? item)
    {
        if (item is null || Runtime.IsRunning)
        {
            return;
        }

        try
        {
            Runtime.SwitchProfile(item.Id);
            RefreshProfiles();
            RefreshEffects();
            AppendActivity($"Juego: {Runtime.Profile.DisplayName}");
        }
        catch (Exception ex)
        {
            AppendActivity(ex.Message);
        }
    }

    [RelayCommand]
    private void SaveHost()
    {
        try
        {
            Runtime.SaveGameHost(GameHost);
            AppendActivity($"Juego {Runtime.Options.GameHost}:{Runtime.Options.GamePort}");
        }
        catch (Exception ex)
        {
            AppendActivity(ex.Message);
        }
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (Runtime.IsRunning)
        {
            await StopAsync().ConfigureAwait(true);
            return;
        }

        _busy = true;
        CanConnect = false;
        StatusText = "Conectando…";
        try
        {
            Runtime.SaveGameHost(GameHost);
            if (TikTokEnabled)
            {
                var user = TikTokUser.Trim().TrimStart('@');
                if (!string.IsNullOrWhiteSpace(user))
                {
                    Runtime.SaveChannel(user);
                    TikTokUser = Runtime.Options.TikTokUniqueId;
                }
            }

            Runtime.SetTikTokEnabled(TikTokEnabled);
            Runtime.SetTwitchEnabled(TwitchEnabled);
            await Runtime.StartAsync(BridgeRunMode.Live).ConfigureAwait(true);
            StartKeepAlive();
            _ = WatchAsync();
            AppendActivity("Conectado · notificación en segundo plano");
        }
        catch (Exception ex)
        {
            AppendActivity(ex.Message);
            StatusText = ex.Message;
        }
        finally
        {
            _busy = false;
            RefreshStatus();
        }
    }

    [RelayCommand]
    private async Task SendEffectAsync()
    {
        if (SelectedEffect is null)
        {
            AppendActivity("Elige un efecto.");
            return;
        }

        try
        {
            Runtime.SaveGameHost(GameHost);
            await Runtime.SendEffectAsync(SelectedEffect.Id).ConfigureAwait(true);
            AppendActivity($"Enviado {SelectedEffect.Display}");
        }
        catch (Exception ex)
        {
            AppendActivity(ex.Message);
        }
    }

    [RelayCommand]
    private async Task ConnectTwitchAsync()
    {
        _twitchCts?.Cancel();
        _twitchCts = new CancellationTokenSource();
        var ct = _twitchCts.Token;
        try
        {
            var progress = new Progress<TwitchDeviceStart>(start =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    TwitchDeviceCode = start.UserCode;
                    TwitchDeviceHint = start.VerificationUri;
                    TwitchCodeVisible = true;
                });
            });
            await Runtime.LoginTwitchAsync(progress, ct).ConfigureAwait(true);
            TwitchCodeVisible = false;
            RefreshTwitchAccount();
            AppendActivity($"Twitch {Runtime.Options.TwitchUserLogin}");
        }
        catch (OperationCanceledException)
        {
            TwitchCodeVisible = false;
        }
        catch (Exception ex)
        {
            TwitchCodeVisible = false;
            AppendActivity(ex.Message);
        }
    }

    [RelayCommand]
    private void DisconnectTwitch()
    {
        Runtime.LogoutTwitch();
        RefreshTwitchAccount();
        AppendActivity("Twitch cerrado");
    }

    [RelayCommand]
    private void CancelTwitch()
    {
        _twitchCts?.Cancel();
        TwitchCodeVisible = false;
    }

    public void Dispose()
    {
        Runtime.Ports.Changed -= OnPortsChanged;
        BridgeLog.Logged -= OnLogged;
        _twitchCts?.Cancel();
        _twitchCts?.Dispose();
    }

    private async Task StopAsync()
    {
        StopKeepAlive();
        try
        {
            await Runtime.StopAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // disconnect
        }

        RefreshStatus();
    }

    private async Task WatchAsync()
    {
        try
        {
            await Runtime.WaitUntilStoppedAsync().ConfigureAwait(true);
        }
        catch
        {
            // ignore
        }

        StopKeepAlive();
        Dispatcher.UIThread.Post(RefreshStatus);
    }

    private void StartKeepAlive()
    {
        try
        {
            BridgeKeepAliveService.Start();
        }
        catch (Exception ex)
        {
            AppendActivity($"Segundo plano: {ex.Message}");
        }
    }

    private static void StopKeepAlive()
    {
        try
        {
            BridgeKeepAliveService.Stop();
        }
        catch
        {
            // ya parado
        }
    }

    private void RefreshProfiles()
    {
        Profiles.Clear();
        foreach (var p in Runtime.ListProfiles())
        {
            Profiles.Add(new ProfileItem
            {
                Id = p.Id,
                Title = p.ShortDisplayName,
                IsSelected = p.Id.Equals(Runtime.Profile.Id, StringComparison.OrdinalIgnoreCase),
            });
        }
    }

    private void RefreshEffects()
    {
        Effects.Clear();
        foreach (var (id, label) in Runtime.Effects.ListEntries())
        {
            Effects.Add(new EffectItem { Id = id, Display = label });
        }

        SelectedEffect = Effects.FirstOrDefault(e => e.Id == "heal") ?? Effects.FirstOrDefault();
    }

    private void RefreshTwitchAccount()
    {
        TwitchAccount = string.IsNullOrWhiteSpace(Runtime.Options.TwitchUserLogin)
            ? "Sin cuenta"
            : $"@{Runtime.Options.TwitchUserLogin}";
    }

    private void RefreshStatus()
    {
        var running = Runtime.IsRunning;
        IsRunning = running;
        CanEdit = !running;
        CanConnect = true;
        IsLive = Runtime.Ports.AnyLive();
        ConnectLabel = running ? "Detener" : "Conectar";
        if (!running)
        {
            StatusText = "Listo";
            return;
        }

        StatusText = IsLive
            ? "En vivo"
            : "Conectado";
        var ports = Runtime.Ports.StatusSummary();
        if (!string.IsNullOrWhiteSpace(ports))
        {
            StatusText = ports;
        }
    }

    private void OnPortsChanged() => Dispatcher.UIThread.Post(RefreshStatus);

    private void OnLogged(LogEntry entry)
    {
        Dispatcher.UIThread.Post(() => AppendActivity(entry.Message));
    }

    private void AppendActivity(string line)
    {
        _activity.Insert(0, $"{DateTime.Now:HH:mm:ss}  {line}");
        while (_activity.Count > MaxActivity)
        {
            _activity.RemoveAt(_activity.Count - 1);
        }

        ActivityText = string.Join(Environment.NewLine, _activity);
    }
}
