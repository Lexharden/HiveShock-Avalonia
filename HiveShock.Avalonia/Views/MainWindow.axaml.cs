using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
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
            TitleBarActions.Margin = new Thickness(0, 0, CaptionFallbackWidth, 0);
            // El ancho real depende del tema, del escalado y de qué botones muestre el sistema:
            // se mide sobre los botones dibujados en cuanto existen y cada vez que cambian.
            Opened += (_, _) => Dispatcher.UIThread.Post(TrackCaptionButtons, DispatcherPriority.Loaded);
            PropertyChanged += (_, e) =>
            {
                if (e.Property == WindowStateProperty)
                {
                    Dispatcher.UIThread.Post(TrackCaptionButtons, DispatcherPriority.Loaded);
                }
            };
        }
    }

    private readonly List<Control> _captionButtons = [];

    /// <summary>Busca los botones del sistema (cambian al maximizar o restaurar) y sigue sus cambios de tamaño.</summary>
    private void TrackCaptionButtons()
    {
        foreach (var c in global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(this))
        {
            if (c is Control { Name: { } name } ctl && Array.IndexOf(CaptionButtonNames, name) >= 0 &&
                !_captionButtons.Contains(ctl))
            {
                _captionButtons.Add(ctl);
                ctl.PropertyChanged += (_, e) =>
                {
                    if (e.Property == BoundsProperty || e.Property == IsVisibleProperty)
                    {
                        FitTitleBarToCaptionButtons();
                    }
                };
            }
        }

        FitTitleBarToCaptionButtons();
    }

    /// <summary>Minimizar + maximizar + cerrar de Avalonia 12 (≈46 px cada uno) con margen.</summary>
    private const double CaptionFallbackWidth = 150;

    private static readonly string[] CaptionButtonNames =
        ["PART_FullScreenButton", "PART_MinimizeButton", "PART_MaximizeButton", "PART_RestoreButton", "PART_CloseButton"];

    /// <summary>
    /// Deja a la derecha justo el hueco que ocupan los botones del sistema visibles (más 8 px de aire), para que
    /// ningún botón de la barra propia quede debajo. Si no se encuentran, se queda el ancho de reserva.
    /// </summary>
    private void FitTitleBarToCaptionButtons()
    {
        if (Bounds.Width <= 0)
        {
            return;
        }

        double? left = null;
        foreach (var ctl in _captionButtons)
        {
            if (!ctl.IsEffectivelyVisible || ctl.Bounds.Width <= 0 || ctl.TranslatePoint(default, this) is not { } p)
            {
                continue;
            }

            left = left is { } l ? Math.Min(l, p.X) : p.X;
        }

        var reserve = left is { } x && x > Bounds.Width / 2
            ? Math.Ceiling(Bounds.Width - x) + 8
            : CaptionFallbackWidth;
        if (Math.Abs(TitleBarActions.Margin.Right - reserve) > 0.5)
        {
            TitleBarActions.Margin = new Thickness(0, 0, reserve, 0);
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
