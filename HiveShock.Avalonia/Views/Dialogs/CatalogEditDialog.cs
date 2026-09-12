using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using HiveShock.Configuration;

namespace HiveShock.Avalonia.Views.Dialogs;

public sealed class CatalogEditDialog : Window
{
    public CatalogGift? Result { get; private set; }

    public CatalogEditDialog(CatalogGift? existing = null)
    {
        Title = existing == null ? "Nuevo regalo" : "Editar regalo";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        var nameEn = Field(existing?.NameEn ?? "");
        var nameEs = Field(existing?.NameEs ?? "");
        var idBox = Field(existing?.Id ?? "");
        var diamondsBox = Field(existing?.Diamonds?.ToString() ?? "");

        var root = new StackPanel { Margin = new Thickness(22) };
        root.Children.Add(new TextBlock
        {
            Text = existing == null ? "Añadir a vistos" : "Editar en vistos",
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 14),
        });
        root.Children.Add(Label("Nombre en inglés"));
        root.Children.Add(nameEn);
        root.Children.Add(Label("Nombre en español"));
        root.Children.Add(nameEs);
        root.Children.Add(Label("ID en TikTok (opcional)"));
        root.Children.Add(idBox);
        root.Children.Add(Label("Diamantes (opcional)"));
        root.Children.Add(diamondsBox);
        root.Children.Add(new TextBlock
        {
            Text = "Con el nombre basta para reconocerlo en el live. El ID solo hace falta si dos regalos se llaman igual.",
            FontSize = 12,
            Opacity = 0.75,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 16),
        });

        var cancel = new Button { Content = "Cancelar", Classes = { "secondary" }, IsCancel = true };
        cancel.Click += (_, _) => Close(false);
        var ok = new Button { Content = "Guardar", Classes = { "primary" }, IsDefault = true, MinWidth = 100 };
        ok.Click += async (_, _) =>
        {
            var nameEnText = nameEn.Text?.Trim() ?? "";
            var nameEsText = nameEs.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(nameEnText) && string.IsNullOrWhiteSpace(nameEsText))
            {
                    var warn = new MessageDialog("Vistos", "Pon al menos un nombre.", confirm: false, kind: "warn");
                await warn.ShowDialog<bool>(this);
                return;
            }

            int? diamonds = null;
            var diamondsRaw = diamondsBox.Text?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(diamondsRaw))
            {
                if (!int.TryParse(diamondsRaw, out var d) || d < 0)
                {
                    var warn = new MessageDialog("Vistos", "Diamantes debe ser un número de 0 en adelante.", confirm: false, kind: "warn");
                    await warn.ShowDialog<bool>(this);
                    return;
                }

                diamonds = d;
            }

            Result = new CatalogGift
            {
                Id = idBox.Text?.Trim() ?? "",
                NameEn = string.IsNullOrWhiteSpace(nameEnText) ? null : nameEnText,
                NameEs = string.IsNullOrWhiteSpace(nameEsText) ? null : nameEsText,
                Diamonds = diamonds,
            };
            Close(true);
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        root.Children.Add(buttons);
        Content = root;
        Opened += (_, _) => nameEn.Focus();
    }

    private static TextBlock Label(string text) =>
        new()
        {
            Text = text,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        };

    private static TextBox Field(string text) =>
        new()
        {
            Text = text,
            Margin = new Thickness(0, 0, 0, 12),
        };
}
