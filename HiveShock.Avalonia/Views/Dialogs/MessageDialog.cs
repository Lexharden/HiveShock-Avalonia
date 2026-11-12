using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace HiveShock.Avalonia.Views.Dialogs;

public sealed class MessageDialog : Window
{
    /// <param name="okText">Texto del botón principal; por defecto «Sí» (confirmar) o «Aceptar».</param>
    /// <param name="cancelText">Texto del botón de cancelar; por defecto «No».</param>
    public MessageDialog(string title, string message, bool confirm, string kind, string? okText = null, string? cancelText = null)
    {
        Title = title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        var root = new StackPanel { Margin = new Thickness(22) };
        root.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 18),
        });

        var buttons = new StackPanel
        {
            Classes = { "bar" },
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };

        if (confirm)
        {
            var cancel = new Button { Content = cancelText ?? "No", Classes = { "secondary" }, IsCancel = true };
            cancel.Click += (_, _) => Close(false);
            buttons.Children.Add(cancel);
        }

        var ok = new Button
        {
            Content = okText ?? (confirm ? "Sí" : "Aceptar"),
            Classes = { kind == "danger" || kind == "warn" ? "danger" : "primary" },
            IsDefault = true,
            MinWidth = 88,
        };
        ok.Click += (_, _) => Close(true);
        buttons.Children.Add(ok);
        root.Children.Add(buttons);
        Content = root;

        // El teclado va al diálogo: Intro acepta y Esc cancela aunque se haya abierto con un atajo (p. ej. Supr).
        Opened += (_, _) =>
        {
            Activate();
            ok.Focus();
        };
    }
}
