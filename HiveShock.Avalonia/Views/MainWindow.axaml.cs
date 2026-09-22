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
        vm.ResizeActivityPanel(current.Y - last.Y);
        _activityDragLast = current;
    }

    private void ActivityResizeHandle_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
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
