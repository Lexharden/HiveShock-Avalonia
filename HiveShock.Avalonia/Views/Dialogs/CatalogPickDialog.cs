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

        var visible = new System.Collections.ObjectModel.ObservableCollection<CatalogGift>(items);
        var list = new ListBox { ItemsSource = visible };
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
            var seen = gift.Seen > 0 ? $" · visto {gift.Seen} {(gift.Seen == 1 ? "vez" : "veces")}" : "";
            row.Children.Add(new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = gift.DisplayName, FontWeight = FontWeight.SemiBold },
                    new TextBlock
                    {
                        Text = $"{diamonds} diamantes{seen}",
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
            Text = "Catálogo de regalos",
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        };

        var count = new TextBlock { FontSize = 12, Opacity = 0.75, Margin = new Thickness(2, 6, 0, 8) };
        var search = new TextBox { PlaceholderText = "Buscar por nombre o id…" };
        void Filter()
        {
            var query = search.Text;
            visible.Clear();
            foreach (var gift in items.Where(g => g.Matches(query)))
            {
                visible.Add(gift);
            }

            count.Text = string.IsNullOrWhiteSpace(query)
                ? $"{items.Count} regalos"
                : $"{visible.Count} de {items.Count} regalos";
            if (visible.Count > 0)
            {
                list.SelectedIndex = 0;
            }
        }

        search.TextChanged += (_, _) => Filter();
        search.KeyDown += (_, e) =>
        {
            // Enter elige el primero; flecha abajo pasa a la lista para moverse con el teclado.
            if (e.Key == Key.Enter)
            {
                Confirm(list);
                e.Handled = true;
            }
            else if (e.Key == Key.Down && visible.Count > 0)
            {
                list.Focus();
                e.Handled = true;
            }
        };
        Opened += (_, _) => search.Focus();
        Filter();

        var header = new StackPanel { Children = { title, search, count } };

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
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(header);
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
