using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace HiveShock.Avalonia.ViewModels;

/// <summary>Inicio · overlays web para OBS («Fuente de navegador»): servidor local, puerto y direcciones.</summary>
public sealed partial class StudioViewModel
{
    private void LoadWebPrefs()
    {
        _loading = true;
        WebOverlayEnabled = _shell.Prefs.WebOverlayEnabled;
        WebOverlayLan = _shell.Prefs.WebOverlayLan;
        WebOverlayPortText = _shell.Prefs.ResolveWebOverlayPort().ToString();
        _loading = false;
        RefreshWebStatus();
    }

    [ObservableProperty] private bool _webOverlayEnabled = true;
    [ObservableProperty] private bool _webOverlayLan;
    [ObservableProperty] private string _webOverlayPortText = "7878";
    [ObservableProperty] private string _webOverlayStatus = "";
    [ObservableProperty] private bool _webOverlayRunning;
    [ObservableProperty] private string _counterUrl = "";
    [ObservableProperty] private string _giftsUrl = "";
    [ObservableProperty] private string _goalsUrl = "";
    [ObservableProperty] private string _alertsUrl = "";

    partial void OnWebOverlayEnabledChanged(bool value) => ApplyWeb();

    partial void OnWebOverlayLanChanged(bool value) => ApplyWeb();

    [RelayCommand]
    private void ApplyWeb()
    {
        if (_loading)
        {
            return;
        }

        if (!int.TryParse(WebOverlayPortText.Trim(), out var port) || port is < 1024 or > 65535)
        {
            WebOverlayStatus = "El puerto debe estar entre 1024 y 65535.";
            return;
        }

        _shell.Prefs.WebOverlayEnabled = WebOverlayEnabled;
        _shell.Prefs.WebOverlayLan = WebOverlayLan;
        _shell.Prefs.WebOverlayPort = port;
        _shell.Prefs.Save();
        _shell.Overlays.StartWeb(_shell.Prefs);
        _shell.Overlays.PublishWeb(
            _shell.Prefs, _shell.Runtime.DeathCounter, _shell.Gifts.Models, _shell.Runtime.Goals.Snapshots());
        RefreshWebStatus();
    }

    public void RefreshWebStatus()
    {
        var web = _shell.Overlays.Web;
        WebOverlayRunning = web.IsRunning;
        if (!WebOverlayEnabled)
        {
            WebOverlayStatus = "Desactivado.";
        }
        else if (!web.IsRunning)
        {
            WebOverlayStatus = $"No se pudo abrir el puerto {WebOverlayPortText}: está ocupado. Prueba con otro número y pulsa Aplicar.";
        }
        else
        {
            WebOverlayStatus = web.AllowLan
                ? "Activo en la red local. Solo funciona con la clave que lleva la dirección: no la compartas."
                : "Activo solo en este equipo.";
        }

        var host = web.AllowLan ? LanAddress() : null;
        CounterUrl = web.IsRunning ? web.UrlFor("contador", host) : "";
        GiftsUrl = web.IsRunning ? web.UrlFor("regalos", host) : "";
        GoalsUrl = web.IsRunning ? web.UrlFor("metas", host) : "";
        AlertsUrl = web.IsRunning ? web.UrlFor("alertas", host) : "";
    }

    private static string? LanAddress()
    {
        try
        {
            return System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up &&
                            n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Select(a => a.Address)
                .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                ?.ToString();
        }
        catch (Exception)
        {
            return null;
        }
    }

    [RelayCommand]
    private async Task CopyUrlAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        await _shell.Overlays.CopyAsync(url);
        WebOverlayStatus = "Dirección copiada. En OBS: Fuentes → + → Navegador → pega la dirección.";
    }

    [RelayCommand]
    private void TestAlert()
    {
        _shell.Overlays.Feed?.Alert("gift", "Espectador", "Rosa", 3, null);
        _shell.Overlays.Feed?.Alert("follow", "Espectador", "", 1, null);
    }
}
