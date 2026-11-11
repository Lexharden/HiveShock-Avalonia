using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using HiveShock.Avalonia.ViewModels;

namespace HiveShock.Avalonia.Views;

public partial class MainWindow : Window
{
    private bool _forceClose;
    private bool _isClosing;
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

    private async void Window_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (_forceClose)
        {
            return;
        }

        e.Cancel = true;
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        if (DataContext is MainViewModel vm)
        {
            // Si algo no se pudo guardar (p. ej. una macro con id repetido) se avisa en vez de perderlo en silencio.
            if (!HiveShock.Avalonia.Services.AutoSaver.FlushAll(out var pendingError))
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
