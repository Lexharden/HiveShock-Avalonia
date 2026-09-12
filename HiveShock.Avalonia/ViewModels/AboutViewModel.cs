using System.Diagnostics;
using System.Reflection;
using CommunityToolkit.Mvvm.Input;
using HiveShock.Configuration;

namespace HiveShock.Avalonia.ViewModels;

public sealed partial class AboutViewModel : ViewModelBase
{
    public string AppName => ProductInfo.Name;
    public string Developer => ProductInfo.Vendor;
    public string WebsiteDisplay => ProductInfo.Website.Replace("https://", "");
    public string WebsiteUrl => ProductInfo.Website;
    public string KoFiUrl => "https://ko-fi.com/yafel";
    public string Version { get; } = ReadVersion();

    public string VersionLabel => $"Versión {Version}";

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

    [RelayCommand]
    private void OpenWebsite() => Open(WebsiteUrl);

    [RelayCommand]
    private void OpenKofi() => Open(KoFiUrl);

    private static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch
        {
            // ignore
        }
    }
}
