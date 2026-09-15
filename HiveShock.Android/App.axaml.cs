using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using HiveShock.Android.ViewModels;
using HiveShock.Android.Views;

namespace HiveShock.Android;

public partial class App : Avalonia.Application
{
    private MainViewModel? _main;

    public override void Initialize()
    {
        AndroidIntents.Register();
        AvaloniaXamlLoader.Load(this);
        RequestedThemeVariant = ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        AndroidAssetSeed.CopyIfNeeded();
        _main ??= new MainViewModel();

        if (ApplicationLifetime is IActivityApplicationLifetime activity)
        {
            activity.MainViewFactory = () => new MainView { DataContext = _main };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime single)
        {
            single.MainView = new MainView { DataContext = _main };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
