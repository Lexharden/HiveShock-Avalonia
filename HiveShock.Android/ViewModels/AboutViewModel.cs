using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HiveShock.Configuration;

namespace HiveShock.Android.ViewModels;

public sealed partial class AboutViewModel : ObservableObject
{
    public string AppName => ProductInfo.Name;
    public string Developer => ProductInfo.Vendor;
    public string WebsiteUrl => ProductInfo.Website;
    public string KoFiUrl => "https://ko-fi.com/yafel";
    public string VersionLabel => $"Versión {ReadVersion()}";

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
