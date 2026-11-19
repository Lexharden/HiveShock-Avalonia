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
using HiveShock.Logging;

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
            // El proceso va a terminar sí o sí: al menos que quede en el log por qué.
            if (args.ExceptionObject is Exception ex && !IsBenignCancellation(ex))
            {
                BridgeLog.Init();
                BridgeLog.Error($"Error fatal: {ex}");
            }
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            if (!args.Exception.InnerExceptions.All(IsBenignCancellation) &&
                !IsBenignCancellation(args.Exception))
            {
                BridgeLog.Error($"Error en segundo plano sin observar: {args.Exception.GetBaseException()}");
            }

            // Ya registrado: no debe tumbar nada.
            args.SetObserved();
        };

        var prefs = UiPreferences.Load();
        ThemeManager.Apply(this, prefs.ResolveTheme(), prefs.FollowsSystem);

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
            WireBackgroundMode(desktop, window, vm);

            // Un poco después de abrir, con la ventana ya maquetada: novedades si se acaba de actualizar
            // y luego el tutorial si nunca se vio.
            window.Opened += (_, _) => DispatcherTimer.RunOnce(
                () => _ = vm.ShowStartupGuidesAsync(), TimeSpan.FromMilliseconds(700));
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Segundo plano: icono en la bandeja, otra copia que se abre (instancia única), Dock de macOS y la cuenta
    /// atrás de «Reiniciar el equipo», que debe verse aunque la ventana esté escondida para poder cancelarla.
    /// </summary>
    private void WireBackgroundMode(IClassicDesktopStyleApplicationLifetime desktop, MainWindow window, MainViewModel vm)
    {
        var tray = new TrayService(vm, window.ShowFromBackground, window.QuitApplication);
        desktop.Exit += (_, _) => tray.Dispose();

        SingleInstance.Listen(() => Dispatcher.UIThread.Post(window.ShowFromBackground));

        if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
        {
            activatable.Activated += (_, e) =>
            {
                if (e.Kind == ActivationKind.Reopen)
                {
                    window.ShowFromBackground();
                }
            };
        }

        vm.SystemFx.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SystemEffectsViewModel.IsCounting) && vm.SystemFx.IsCounting)
            {
                Dispatcher.UIThread.Post(window.ShowFromBackground);
            }
        };
    }

    private bool _errorDialogOpen;

    /// <summary>
    /// Un fallo de la interfaz no cierra HiveShock: en pleno directo, perder el puente (y los efectos en cola)
    /// es peor que un botón que falló. Se registra con su traza, se avisa una vez y la app sigue.
    /// </summary>
    private async void OnUiUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs args)
    {
        args.Handled = true;
        if (IsBenignCancellation(args.Exception))
        {
            return;
        }

        BridgeLog.Error($"Error en la interfaz: {args.Exception}");

        // Un fallo que se repite (p. ej. en cada repintado) no debe apilar diálogos.
        if (_errorDialogOpen)
        {
            return;
        }

        _errorDialogOpen = true;
        try
        {
            var dlg = new MessageDialog(
                "HiveShock",
                $"Algo falló en la ventana: {args.Exception.Message}\n\nHiveShock sigue funcionando. " +
                "Si vuelve a pasar, reinícialo y envíanos el log.",
                confirm: false,
                kind: "danger");
            dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { IsVisible: true } main })
            {
                await dlg.ShowDialog<bool>(main);
            }
            else
            {
                var closed = new TaskCompletionSource();
                dlg.Closed += (_, _) => closed.TrySetResult();
                dlg.Show();
                await closed.Task;
            }
        }
        catch (Exception ex)
        {
            BridgeLog.Warn($"No se pudo mostrar el aviso de error: {ex.Message}");
        }
        finally
        {
            _errorDialogOpen = false;
        }
    }

    private static bool IsBenignCancellation(Exception ex) =>
        ex is OperationCanceledException or TaskCanceledException
        || ex.GetBaseException() is OperationCanceledException or TaskCanceledException;
}
