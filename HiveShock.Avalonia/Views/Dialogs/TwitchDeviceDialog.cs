using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using HiveShock.Live;

namespace HiveShock.Avalonia.Views.Dialogs;

public sealed class TwitchDeviceDialog : Window
{
    private readonly TextBlock _code;
    private readonly TextBlock _hint;

    public TwitchDeviceDialog()
    {
        Title = "Twitch";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        var root = new StackPanel { Margin = new Thickness(22) };
        root.Children.Add(new TextBlock
        {
            Text = "Escribe este código en la página que se abrió:",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        });
        _code = new TextBlock
        {
            FontSize = 28,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12),
        };
        root.Children.Add(_code);
        _hint = new TextBlock
        {
            Text = "Si el navegador no abre, ve a twitch.tv/activate",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.8,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 16),
        };
        root.Children.Add(_hint);

        var cancel = new Button
        {
            Content = "Cancelar",
            Classes = { "secondary" },
            HorizontalAlignment = HorizontalAlignment.Right,
            IsCancel = true,
            MinWidth = 88,
        };
        cancel.Click += (_, _) => Close(false);
        root.Children.Add(cancel);
        Content = root;
        Closed += (_, _) => { };
    }

    public void ShowStart(TwitchDeviceStart start)
    {
        _code.Text = start.UserCode;
        _hint.Text = $"Se abre el navegador. Si no, ve a {start.VerificationUri}";
    }
}
