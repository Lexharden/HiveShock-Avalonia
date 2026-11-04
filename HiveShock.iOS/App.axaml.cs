using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using HiveShock.Mobile.ViewModels;
using HiveShock.Mobile.Views;

namespace HiveShock.iOS;

public partial class App : Avalonia.Application
{
    private MainViewModel? _main;

    public override void Initialize()
    {
        IosHost.Register();
        AvaloniaXamlLoader.Load(this);
        RequestedThemeVariant = ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        IosAssetSeed.CopyIfNeeded();
        _main ??= new MainViewModel();

        if (ApplicationLifetime is ISingleViewApplicationLifetime single)
        {
            single.MainView = new MainView { DataContext = _main };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
