using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using HiveShock.Avalonia.Services;
using HiveShock.Avalonia.Themes;
using HiveShock.Avalonia.ViewModels;
using HiveShock.Avalonia.Views;
using HiveShock.Avalonia.Views.Dialogs;

namespace HiveShock.Avalonia;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Dispatcher.UIThread.UnhandledException += OnUiUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is OperationCanceledException)
            {
                return;
            }
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            if (args.Exception.InnerExceptions.All(static ex => ex is OperationCanceledException) ||
                args.Exception.GetBaseException() is OperationCanceledException)
            {
                args.SetObserved();
            }
        };

        var prefs = UiPreferences.Load();
        ThemeManager.Apply(this, prefs.ResolveTheme());

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var dialogs = new DialogService();
            var overlays = new OverlayService();
            var vm = new MainViewModel(dialogs, overlays);
            var window = new MainWindow { DataContext = vm };
            dialogs.Owner = window;
            overlays.Owner = window;
            desktop.MainWindow = window;
            // Show de la principal ocurre al arrancar el lifetime; overlays después de Opened.
            window.Opened += (_, _) => vm.Attach();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async void OnUiUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs args)
    {
        if (IsBenignCancellation(args.Exception))
        {
            args.Handled = true;
            return;
        }

        args.Handled = true;
        try
        {
            var dlg = new MessageDialog("HiveShock", args.Exception.Message, confirm: false, kind: "danger");
            dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { IsVisible: true } main })
            {
                await dlg.ShowDialog<bool>(main);
            }
            else
            {
                dlg.Show();
            }
        }
        catch
        {
            // ignore
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown(1);
        }
    }

    private static bool IsBenignCancellation(Exception ex) =>
        ex is OperationCanceledException or TaskCanceledException
        || ex.GetBaseException() is OperationCanceledException or TaskCanceledException;
}
