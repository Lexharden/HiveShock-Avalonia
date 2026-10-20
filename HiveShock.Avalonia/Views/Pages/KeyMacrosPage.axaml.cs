using Avalonia.Controls;
using Avalonia.Input;
using HiveShock.Avalonia.ViewModels;

namespace HiveShock.Avalonia.Views.Pages;

public partial class KeyMacrosPage : UserControl
{
    public KeyMacrosPage()
    {
        InitializeComponent();
        // Túnel: la captura debe ver la tecla antes que el botón o el cuadro de texto enfocados.
        AddHandler(KeyDownEvent, OnKeyDown, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private KeyMacrosViewModel? Vm => DataContext as KeyMacrosViewModel;

    protected override void OnAttachedToVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Vm?.StartMonitoring();
    }

    protected override void OnDetachedFromVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
    {
        Vm?.StopMonitoring();
        Vm?.FlushPending();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (Vm?.CompleteCapture(e.Key, e.KeyModifiers) == true)
        {
            e.Handled = true;
        }
    }
}
