using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using HiveShock.Avalonia.Services;
using HiveShock.Configuration;

namespace HiveShock.Avalonia.Views.Dialogs;

public sealed class CatalogPickDialog : Window
{
    public CatalogGift? Selected { get; private set; }

    public CatalogPickDialog(IReadOnlyList<CatalogGift> items)
    {
        Title = "Elegir un regalo";
        Width = 560;
        Height = 480;
        MinWidth = 420;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = true;

        var list = new ListBox { ItemsSource = items };
        list.ItemTemplate = new FuncDataTemplate<CatalogGift>((gift, _) =>
        {
            var row = new DockPanel { Margin = new Thickness(4, 6) };
            var image = new Image
            {
                Width = 36,
                Height = 36,
                Margin = new Thickness(0, 0, 10, 0),
                Stretch = Stretch.Uniform,
                Source = GiftImageLoader.Load(gift.ImagePath),
            };
            DockPanel.SetDock(image, Dock.Left);
            row.Children.Add(image);
            var diamonds = gift.Diamonds?.ToString() ?? "—";
            row.Children.Add(new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = gift.DisplayName, FontWeight = FontWeight.SemiBold },
                    new TextBlock
                    {
                        Text = $"{diamonds} diamantes",
                        FontSize = 12,
                        Opacity = 0.75,
                    },
                },
            });
            return row;
        }, true);

        list.DoubleTapped += (_, _) => Confirm(list);
        list.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Confirm(list);
            }
        };

        var title = new TextBlock
        {
            Text = "Regalos que ya salieron en el live",
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 12),
        };

        var cancel = new Button { Content = "Cancelar", Classes = { "secondary" }, IsCancel = true };
        cancel.Click += (_, _) => Close(false);
        var ok = new Button { Content = "Usar este", Classes = { "primary" }, IsDefault = true, MinWidth = 110 };
        ok.Click += (_, _) => Confirm(list);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0),
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        var root = new DockPanel { Margin = new Thickness(18) };
        DockPanel.SetDock(title, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(title);
        root.Children.Add(buttons);
        root.Children.Add(list);
        Content = root;
    }

    private void Confirm(ListBox list)
    {
        if (list.SelectedItem is not CatalogGift gift)
        {
            return;
        }

        Selected = gift;
        Close(true);
    }
}
