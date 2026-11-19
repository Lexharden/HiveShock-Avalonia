using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using HiveShock.Updates;

namespace HiveShock.Avalonia.Views.Controls;

/// <summary>
/// Notas de versión (Markdown del CHANGELOG o de GitHub) con el aspecto de la app: títulos, viñetas, citas
/// destacadas y negritas. Se usa en «Novedades» y en la lista de versiones de Actualizaciones.
/// </summary>
public sealed class NotesView : StackPanel
{
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<NotesView, string?>(nameof(Markdown));

    /// <summary>Si es mayor que 0, solo los primeros N bloques (un vistazo que corta entre líneas, nunca a mitad).</summary>
    public static readonly StyledProperty<int> MaxBlocksProperty =
        AvaloniaProperty.Register<NotesView, int>(nameof(MaxBlocks));

    static NotesView()
    {
        MarkdownProperty.Changed.AddClassHandler<NotesView>((view, _) => view.Rebuild());
        MaxBlocksProperty.Changed.AddClassHandler<NotesView>((view, _) => view.Rebuild());
    }

    public NotesView()
    {
        Spacing = 6;
    }

    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public int MaxBlocks
    {
        get => GetValue(MaxBlocksProperty);
        set => SetValue(MaxBlocksProperty, value);
    }

    private void Rebuild()
    {
        Children.Clear();
        var first = true;
        var blocks = NotesMarkdown.Parse(Markdown);
        foreach (var block in MaxBlocks > 0 ? blocks.Take(MaxBlocks) : blocks)
        {
            Children.Add(Build(block, first));
            first = false;
        }
    }

    private static Control Build(NotesBlock block, bool first)
    {
        switch (block.Kind)
        {
            case NotesBlockKind.Heading:
            {
                var text = Text(block, 11, FontWeight.Bold);
                text.Text = block.PlainText.ToUpperInvariant();
                text.Inlines = null;
                text.LetterSpacing = 0.6;
                text.Margin = new Thickness(0, first ? 0 : 10, 0, 2);
                text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("GoldTextBrush"));
                return text;
            }
            case NotesBlockKind.Bullet:
            {
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("16,*") };
                var dot = new TextBlock { Text = "•", FontSize = 13, Opacity = 0.6 };
                var text = Text(block, 13, FontWeight.Normal);
                Grid.SetColumn(text, 1);
                grid.Children.Add(dot);
                grid.Children.Add(text);
                return grid;
            }
            case NotesBlockKind.Quote:
            {
                var border = new Border
                {
                    Padding = new Thickness(12, 8),
                    CornerRadius = new CornerRadius(8),
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    Margin = new Thickness(0, 2),
                    Child = Text(block, 13, FontWeight.Normal),
                };
                border.Bind(Border.BackgroundProperty, border.GetResourceObservable("GoldSubtleBrush"));
                border.Bind(Border.BorderBrushProperty, border.GetResourceObservable("GoldTextBrush"));
                return border;
            }
            default:
                return Text(block, 13, FontWeight.Normal);
        }
    }

    private static TextBlock Text(NotesBlock block, double size, FontWeight weight)
    {
        var text = new TextBlock
        {
            FontSize = size,
            FontWeight = weight,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = size * 1.45,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Inlines = [],
        };
        foreach (var span in block.Spans)
        {
            text.Inlines!.Add(new Run(span.Text) { FontWeight = span.Bold ? FontWeight.SemiBold : weight });
        }

        return text;
    }
}
