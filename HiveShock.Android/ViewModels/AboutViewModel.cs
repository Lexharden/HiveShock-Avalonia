using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HiveShock.Configuration;
using HiveShock.Logging;
using HiveShock.Updates;

namespace HiveShock.Android.ViewModels;

public sealed partial class AboutViewModel : ObservableObject
{
    public string AppName => ProductInfo.Name;
    public string Developer => ProductInfo.Vendor;
    public string WebsiteUrl => ProductInfo.Website;
    public string KoFiUrl => "https://ko-fi.com/yafel";
    public string VersionLabel => $"Versión {ReadVersion()}";

    private UpdateService? _updates;
    private string _updateUrl = "";

    [ObservableProperty] private string _updateText = "Comprueba si hay una versión nueva.";
    [ObservableProperty] private bool _updateAvailable;
    [ObservableProperty] private bool _checking;

    /// <summary>
    /// Solo avisa: en Android instalar es bajar el APK nuevo, así que se abre la página del release
    /// y la descarga la elige el usuario (igual que en escritorio, nada se baja solo).
    /// </summary>
    [RelayCommand]
    private async Task CheckUpdateAsync()
    {
        if (Checking)
        {
            return;
        }

        Checking = true;
        UpdateAvailable = false;
        UpdateText = "Buscando…";
        try
        {
            _updates ??= new UpdateService(new HttpClient(), ReadVersion(), new UpdateOptions { CheckOnStart = false });
            await _updates.CheckAsync().ConfigureAwait(true);
            if (_updates.Announceable is { } release)
            {
                _updateUrl = release.HtmlUrl;
                UpdateAvailable = true;
                UpdateText = $"Hay una versión nueva: {release.Version}";
            }
            else if (_updates.State == UpdateState.Failed)
            {
                UpdateText = string.IsNullOrWhiteSpace(_updates.Error) ? "No se pudo comprobar." : _updates.Error;
            }
            else
            {
                UpdateText = "Tienes la última versión.";
            }
        }
        catch (Exception ex)
        {
            BridgeLog.Warn($"Actualizaciones: {ex.Message}");
            UpdateText = "No se pudo comprobar. Revisa tu conexión.";
        }
        finally
        {
            Checking = false;
        }
    }

    [RelayCommand]
    private void OpenUpdate()
    {
        if (!string.IsNullOrWhiteSpace(_updateUrl))
        {
            AndroidIntents.OpenUrl(_updateUrl);
        }
    }

    [RelayCommand]
    private void OpenWebsite()
    {
        AndroidIntents.OpenUrl(WebsiteUrl);
    }

    [RelayCommand]
    private void OpenKofi()
    {
        AndroidIntents.OpenUrl(KoFiUrl);
    }

    private static string ReadVersion()
    {
        var informational = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return plus > 0 ? informational[..plus] : informational;
        }

        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v is null ? "1.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }
}
