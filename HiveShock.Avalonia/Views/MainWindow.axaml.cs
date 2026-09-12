using Avalonia.Controls;
using HiveShock.Avalonia.ViewModels;

namespace HiveShock.Avalonia.Views;

public partial class MainWindow : Window
{
    private bool _forceClose;
    private bool _isClosing;

    public MainWindow()
    {
        InitializeComponent();
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
