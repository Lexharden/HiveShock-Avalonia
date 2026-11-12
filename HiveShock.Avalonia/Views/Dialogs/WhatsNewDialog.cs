using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using HiveShock.Avalonia.Views.Controls;
using HiveShock.Updates;

namespace HiveShock.Avalonia.Views.Dialogs;

/// <summary>
/// «Novedades»: las notas de una o varias versiones (de la más nueva a la más antigua), con su fecha.
/// Sale sola una vez al abrir una versión nueva y también desde Ayuda, Acerca de y Actualizaciones.
/// </summary>
public sealed class WhatsNewDialog : Window
{
    public WhatsNewDialog(IReadOnlyList<ChangelogEntry> entries, string heading, string? subtitle = null)
    {
        Title = "Novedades";
        Width = 640;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        var header = new StackPanel { Spacing = 4, Margin = new Thickness(26, 24, 26, 14) };
        header.Children.Add(new TextBlock
        {
            Text = heading,
            FontSize = 20,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            header.Children.Add(new TextBlock { Text = subtitle, FontSize = 13, Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
        }

        var body = new StackPanel { Spacing = 22, Margin = new Thickness(26, 4, 26, 8) };
        var several = entries.Count > 1;
        foreach (var entry in entries)
        {
            var section = new StackPanel { Spacing = 10 };
            if (several)
            {
                section.Children.Add(VersionLine(entry));
            }

            section.Children.Add(new NotesView
            {
                Markdown = string.IsNullOrWhiteSpace(entry.Body) ? "Sin notas para esta versión." : entry.Body,
            });
            body.Children.Add(section);
        }

        var scroll = new ScrollViewer
        {
            Content = body,
            MaxHeight = 520,
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };

        var ok = new Button
        {
            Content = "Entendido",
            Classes = { "primary" },
            IsDefault = true,
            IsCancel = true,
            MinWidth = 110,
        };
        ok.Click += (_, _) => Close(true);
        var footer = new Border
        {
            Padding = new Thickness(26, 14),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Children = { ok },
            },
        };
        footer.Bind(Border.BorderBrushProperty, footer.GetResourceObservable("BorderBrush"));

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(header);
        root.Children.Add(footer);
        root.Children.Add(scroll);
        Content = root;

        Opened += (_, _) =>
        {
            Activate();
            ok.Focus();
        };
    }

    /// <summary>«3.0.0-beta.2 · 12 nov 2026» con la versión destacada, para separar varias versiones.</summary>
    private static Control VersionLine(ChangelogEntry entry)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var pill = new Border
        {
            Padding = new Thickness(10, 3),
            CornerRadius = new CornerRadius(999),
            Child = new TextBlock { Text = entry.Version, FontWeight = FontWeight.SemiBold, FontSize = 13 },
        };
        pill.Bind(Border.BackgroundProperty, pill.GetResourceObservable("CardAltBrush"));
        line.Children.Add(pill);
        if (entry.DateText.Length > 0)
        {
            line.Children.Add(new TextBlock
            {
                Text = entry.DateText,
                FontSize = 12,
                Opacity = 0.65,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        return line;
    }
}
