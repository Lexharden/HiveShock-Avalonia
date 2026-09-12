using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace HiveShock.Avalonia.Views.Dialogs;

public sealed class ConfirmTextDialog : Window
{
    public ConfirmTextDialog(string title, string message, string expected)
    {
        Title = title;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        var box = new TextBox { Margin = new Thickness(0, 0, 0, 16) };
        var root = new StackPanel { Margin = new Thickness(22) };
        root.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        });
        root.Children.Add(box);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };
        var cancel = new Button { Content = "Cancelar", Classes = { "secondary" }, IsCancel = true };
        cancel.Click += (_, _) => Close(false);
        var ok = new Button { Content = "Confirmar", Classes = { "danger" }, IsDefault = true, MinWidth = 100 };
        ok.Click += async (_, _) =>
        {
            if (!string.Equals(box.Text?.Trim(), expected, StringComparison.Ordinal))
            {
                var warn = new MessageDialog(title, $"Escribe {expected} para continuar.", confirm: false, kind: "warn");
                await warn.ShowDialog<bool>(this);
                return;
            }

            Close(true);
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        root.Children.Add(buttons);
        Content = root;
        Opened += (_, _) => box.Focus();
    }
}
