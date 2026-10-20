using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using HiveShock.Configuration;

namespace HiveShock.Avalonia.Views.Dialogs;

/// <summary>Pide el nombre y el punto de partida de un perfil propio nuevo.</summary>
public sealed class NewProfileDialog : Window
{
    public string ProfileName { get; private set; } = "";

    public ProfileTemplate Template { get; private set; } = ProfileTemplate.Blank;

    public NewProfileDialog(string currentProfileName)
    {
        Title = "Nuevo perfil";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        var nameBox = new TextBox
        {
            PlaceholderText = "Ej.: Stream de Minecraft",
            MaxLength = CustomProfileStore.MaxNameLength,
            Margin = new Thickness(0, 0, 0, 14),
        };
        var blank = Option("En blanco", "Sin juego: solo comandos de chat y macros de teclas.", ProfileTemplate.Blank, isChecked: true);
        var starter = Option("Con un ejemplo", $"En blanco, con el comando !{CustomProfileStore.StarterWord} ya enlazado a una macro.", ProfileTemplate.Starter);
        var copy = Option("Copia del perfil actual", $"Parte de «{currentProfileName}»: sus efectos, regalos y comandos.", ProfileTemplate.CopyOf);

        var root = new StackPanel { Margin = new Thickness(22) };
        root.Children.Add(new TextBlock
        {
            Text = "Perfil propio",
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        });
        root.Children.Add(new TextBlock
        {
            Text = "Se guarda en tu carpeta de datos: las actualizaciones no lo tocan y puedes borrarlo cuando quieras.",
            FontSize = 12,
            Opacity = 0.75,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14),
        });
        root.Children.Add(new TextBlock { Text = "Nombre", FontSize = 12, Margin = new Thickness(0, 0, 0, 4) });
        root.Children.Add(nameBox);
        root.Children.Add(new TextBlock { Text = "Punto de partida", FontSize = 12, Margin = new Thickness(0, 0, 0, 4) });
        root.Children.Add(blank);
        root.Children.Add(starter);
        root.Children.Add(copy);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 16, 0, 0),
        };
        var cancel = new Button { Content = "Cancelar", Classes = { "secondary" }, IsCancel = true };
        cancel.Click += (_, _) => Close(false);
        var ok = new Button { Content = "Crear", Classes = { "primary" }, IsDefault = true, MinWidth = 100 };
        ok.Click += async (_, _) =>
        {
            var name = nameBox.Text?.Trim() ?? "";
            if (name.Length == 0)
            {
                var warn = new MessageDialog("Nuevo perfil", "Ponle un nombre al perfil.", confirm: false, kind: "warn");
                await warn.ShowDialog<bool>(this);
                return;
            }

            ProfileName = name;
            Template = copy.IsChecked == true ? ProfileTemplate.CopyOf
                : starter.IsChecked == true ? ProfileTemplate.Starter
                : ProfileTemplate.Blank;
            Close(true);
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        root.Children.Add(buttons);
        Content = root;
        Opened += (_, _) => nameBox.Focus();
    }

    private static RadioButton Option(string title, string hint, ProfileTemplate template, bool isChecked = false)
    {
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold });
        text.Children.Add(new TextBlock
        {
            Text = hint,
            FontSize = 12,
            Opacity = 0.75,
            TextWrapping = TextWrapping.Wrap,
        });
        return new RadioButton
        {
            GroupName = "template",
            IsChecked = isChecked,
            Tag = template,
            Content = text,
            Margin = new Thickness(0, 0, 0, 6),
        };
    }
}
