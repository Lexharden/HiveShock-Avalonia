using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace HiveShock.Avalonia.Views.Dialogs;

public sealed class MessageDialog : Window
{
    public MessageDialog(string title, string message, bool confirm, string kind)
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
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };

        if (confirm)
        {
            var cancel = new Button { Content = "No", Classes = { "secondary" }, IsCancel = true };
            cancel.Click += (_, _) => Close(false);
            buttons.Children.Add(cancel);
        }

        var ok = new Button
        {
            Content = confirm ? "Sí" : "Aceptar",
            Classes = { kind == "danger" || kind == "warn" ? "danger" : "primary" },
            IsDefault = true,
            MinWidth = 88,
        };
        ok.Click += (_, _) => Close(true);
        buttons.Children.Add(ok);
        root.Children.Add(buttons);
        Content = root;
    }
}
