using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using HiveShock.Avalonia.Services;
using HiveShock.Avalonia.ViewModels;
using HiveShock.Avalonia.Views.Dialogs;

namespace HiveShock.Avalonia.Views;

public partial class MainWindow : Window
{
    private bool _forceClose;
    private bool _isClosing;
    private bool _quitRequested;
    private Point? _activityDragLast;

    public MainWindow()
    {
        InitializeComponent();
        ReserveCaptionButtonsSpace();
    }

    /// <summary>
    /// La barra de título es propia (ExtendClientAreaToDecorationsHint). Los botones del sistema quedan encima:
    /// a la derecha en Windows/Linux y a la izquierda (semáforo) en macOS, así que se deja hueco para ellos.
    /// </summary>
    private void ReserveCaptionButtonsSpace()
    {
        if (OperatingSystem.IsMacOS())
        {
            TitleBarContent.Margin = new Thickness(76, 0, 0, 0);
            TitleBarActions.Margin = new Thickness(0, 0, 12, 0);
        }
        else
        {
            TitleBarContent.Margin = new Thickness(8, 0, 0, 0);
            TitleBarActions.Margin = new Thickness(0, 0, 150, 0);
        }
    }

    private void ActivityResizeHandle_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control handle || !e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _activityDragLast = e.GetPosition(this);
        e.Pointer.Capture(handle);
    }

    private void ActivityResizeHandle_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_activityDragLast is not { } last || DataContext is not MainViewModel vm)
        {
            return;
        }

        var current = e.GetPosition(this);
        vm.ResizeActivityPanelWidth(current.X - last.X);
        _activityDragLast = current;
    }

    private void ActivityResizeHandle_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_activityDragLast != null && DataContext is MainViewModel vm)
        {
            vm.CommitActivityPanelWidth();
        }

        _activityDragLast = null;
        e.Pointer.Capture(null);
    }

    // ------------------------------------------------------------------ Segundo plano (bandeja)

    /// <summary>Vuelve a mostrar la ventana (icono de la bandeja, otra copia que se abre, Dock de macOS…).</summary>
    public void ShowFromBackground()
    {
        if (_forceClose)
        {
            return;
        }

        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    private void ReplayTutorial_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        PrefsButton.Flyout?.Hide();
        (DataContext as MainViewModel)?.Tutorial.StartCommand.Execute(null);
    }

    /// <summary>Salir de verdad (menú de la bandeja): mismo camino que cualquier cierre desde código.</summary>
    public void QuitApplication()
    {
        _quitRequested = true;
        Close();
    }

    private void GoToBackground(MainViewModel vm)
    {
        // Lo pendiente se guarda ya: con la ventana escondida es fácil olvidarse y apagar el equipo.
        if (!AutoSaver.FlushAll(out var error))
        {
            vm.LastActivityLine = $"No se pudo guardar: {error}";
        }

        if (TrayService.IsSupported)
        {
            Hide();
        }
        else
        {
            WindowState = WindowState.Minimized;
        }
    }

    /// <summary>
    /// La X del usuario (no un cierre desde código como el del actualizador, ni el apagado del sistema)
    /// deja la app en segundo plano si así está configurado. La primera vez se pregunta.
    /// </summary>
    private async Task<bool> ShouldGoToBackgroundAsync(MainViewModel vm)
    {
        if (vm.Prefs.CloseActionChosen)
        {
            return vm.CloseToTray;
        }

        var dialog = new CloseChoiceDialog(TrayService.IsSupported);
        await dialog.ShowDialog<bool>(this);
        if (dialog.Choice == CloseChoice.Cancel)
        {
            throw new OperationCanceledException();
        }

        vm.CloseToTray = dialog.Choice == CloseChoice.Background;
        vm.Prefs.CloseActionChosen = true;
        vm.Prefs.Save();
        return vm.CloseToTray;
    }

    /// <summary>
    /// ¿Es la X (o Alt+F4) del usuario? No se puede usar <see cref="WindowClosingEventArgs.IsProgrammatic"/>: la barra
    /// de título que dibuja Avalonia 12 cierra llamando a Close() y llega como «desde código». Los cierres que de
    /// verdad deben salir se reconocen por señales propias: «Salir» de la bandeja (<c>_quitRequested</c>), el
    /// actualizador (llama antes a ShutdownAsync → <c>IsShuttingDown</c>) y el apagado del sistema o de la app
    /// (otro <see cref="WindowCloseReason"/>).
    /// </summary>
    private static bool IsUserCloseRequest(WindowClosingEventArgs e) =>
        e.CloseReason == WindowCloseReason.WindowClosing;

    private async void Window_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (_forceClose)
        {
            return;
        }

        // Cancelar siempre antes del primer await: el evento no espera a los métodos async.
        e.Cancel = true;
        HiveShock.Logging.BridgeLog.Info($"Cerrar ventana: motivo={e.CloseReason}, desde código={e.IsProgrammatic}, salir={_quitRequested}");
        if (_isClosing)
        {
            return;
        }

        if (DataContext is MainViewModel shell && !_quitRequested && !shell.IsShuttingDown && IsUserCloseRequest(e))
        {
            _isClosing = true;
            try
            {
                if (await ShouldGoToBackgroundAsync(shell))
                {
                    GoToBackground(shell);
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return; // canceló el diálogo: la ventana sigue abierta
            }
            finally
            {
                _isClosing = false;
            }
        }

        _isClosing = true;
        if (DataContext is MainViewModel vm)
        {
            // Si algo no se pudo guardar (p. ej. una macro con id repetido) se avisa en vez de perderlo en silencio.
            if (!AutoSaver.FlushAll(out var pendingError))
            {
                var closeAnyway = await vm.Dialogs.ConfirmAsync(
                    "Cambios sin guardar",
                    $"No se pudo guardar: {pendingError}{Environment.NewLine}{Environment.NewLine}Si cierras ahora se perderán. ¿Cerrar igualmente?");
                if (!closeAnyway)
                {
                    _isClosing = false;
                    return;
                }
            }

            try
            {
                await vm.ShutdownAsync().ConfigureAwait(true);
            }
            catch
            {
                // ignore
            }
        }

        _forceClose = true;
        Close();
    }
}
