using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using HiveShock.Avalonia.Services;
using HiveShock.Avalonia.Themes;
using HiveShock.Live;

namespace HiveShock.Avalonia.Views.Overlays;

public sealed class GoalOverlayWindow : Window
{
    private const double BaseWidth = 280;
    private const double BaseHeight = 168;
    private const double BaseMinWidth = 180;
    private const double BaseMinHeight = 128;

    private readonly Border _card;
    private readonly StackPanel _stack;
    private double _scale = 1.5;
    private bool _userSized;
    private bool _applyingLayout;
    private UiPreferences _look = new();
    private IReadOnlyList<GoalSnapshot> _goals = [];
    private readonly Dictionary<string, GoalGiftInfo?> _giftCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Busca el regalo (nombre e imagen) de una clave de meta; sin él solo se muestra el texto de la meta.</summary>
    public Func<string, GoalGiftInfo?>? GiftLookup { get; set; }

    public GoalOverlayWindow()
    {
        Title = "Metas — HiveShock";
        SetValue(Window.WindowDecorationsProperty, global::Avalonia.Controls.WindowDecorations.None);
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = OperatingSystem.IsWindows();
        ShowActivated = false;
        CanResize = true;
        Width = BaseWidth * _scale;
        Height = BaseHeight * _scale;

        _card = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
        };
        _stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _card.Child = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _stack,
        };
        Content = _card;

        ThemeManager.ThemeChanged += OnThemeChanged;
        Closed += (_, _) => ThemeManager.ThemeChanged -= OnThemeChanged;
        Resized += (_, _) =>
        {
            if (!_applyingLayout && IsLoaded && IsVisible)
            {
                _userSized = true;
            }
        };
        PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        };
    }

    public void ApplyLook(UiPreferences prefs, double scale)
    {
        _look = prefs;
        _giftCache.Clear();
        ApplyScale(scale);
        Paint();
        Rebuild();
    }

    public void Apply(IEnumerable<GoalSnapshot> goals)
    {
        _goals = goals.ToList();
        Rebuild();
        Paint();
    }

    private void ApplyScale(double scale)
    {
        _applyingLayout = true;
        try
        {
            _scale = Math.Clamp(scale, 1.0, 3.0);
            _card.Padding = new Thickness(22 * _scale, 16 * _scale, 22 * _scale, 18 * _scale);
            _card.Margin = new Thickness(8 * _scale);
            _card.CornerRadius = new CornerRadius(10 * _scale);
            MinWidth = BaseMinWidth * _scale;
            MinHeight = BaseMinHeight * _scale;
            if (!_userSized)
            {
                Width = BaseWidth * _scale;
                Height = BaseHeight * _scale;
            }
            else
            {
                Width = Math.Max(Width, MinWidth);
                Height = Math.Max(Height, MinHeight);
            }
        }
        finally
        {
            _applyingLayout = false;
        }
    }

    private void Rebuild()
    {
        _stack.Children.Clear();
        var align = OverlayLook.AlignCenter(_look) ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        _stack.HorizontalAlignment = align;
        var titleSize = OverlayLook.TitleSize(_look);
        var numberSize = OverlayLook.NumberSize(_look);
        var titleBrush = new SolidColorBrush(OverlayLook.Title(_look));
        var numberBrush = new SolidColorBrush(OverlayLook.Number(_look));

        if (_goals.Count == 0)
        {
            _stack.Children.Add(new TextBlock
            {
                Text = "Sin metas",
                FontSize = titleSize,
                Foreground = titleBrush,
                HorizontalAlignment = align,
            });
            return;
        }

        var many = _goals.Count > 1;
        foreach (var goal in _goals)
        {
            var block = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, many ? 10 * _scale : 0),
                HorizontalAlignment = align,
            };
            var gifts = GiftsOf(goal);
            if (gifts.Count > 0)
            {
                block.Children.Add(BuildGiftRow(gifts, align, titleSize, titleBrush));
            }

            block.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(goal.Label) ? "Meta" : goal.Label,
                FontWeight = FontWeight.SemiBold,
                FontSize = titleSize,
                Foreground = titleBrush,
                HorizontalAlignment = align,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = align == HorizontalAlignment.Center ? TextAlignment.Center : TextAlignment.Left,
                MaxWidth = 240 * _scale,
                Margin = new Thickness(0, 0, 0, 6),
            });
            block.Children.Add(new TextBlock
            {
                Text = goal.ProgressText,
                FontWeight = FontWeight.Bold,
                FontSize = many ? Math.Max(22, numberSize * 0.55) : numberSize,
                Foreground = numberBrush,
                HorizontalAlignment = align,
            });
            _stack.Children.Add(block);
        }
    }

    private List<GoalGiftInfo> GiftsOf(GoalSnapshot goal)
    {
        var list = new List<GoalGiftInfo>();
        foreach (var key in goal.Keys.Where(k => !string.IsNullOrWhiteSpace(k)))
        {
            if (!_giftCache.TryGetValue(key, out var info))
            {
                try
                {
                    info = GiftLookup?.Invoke(key);
                }
                catch (Exception)
                {
                    info = null;
                }

                _giftCache[key] = info;
            }

            // Sin catálogo se enseña al menos el nombre que puso el usuario.
            var shown = info ?? new GoalGiftInfo(key, null);
            if (!list.Any(g => string.Equals(g.Name, shown.Name, StringComparison.OrdinalIgnoreCase)))
            {
                list.Add(shown);
            }
        }

        return list;
    }

    private Control BuildGiftRow(IReadOnlyList<GoalGiftInfo> gifts, HorizontalAlignment align, double titleSize, IBrush titleBrush)
    {
        var size = 40 * _scale;
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6 * _scale,
            HorizontalAlignment = align,
            Margin = new Thickness(0, 0, 0, 4 * _scale),
        };
        foreach (var gift in gifts.Take(4))
        {
            var bmp = GiftImageLoader.Load(gift.ImagePath);
            if (bmp != null)
            {
                row.Children.Add(new Image
                {
                    Source = bmp,
                    Width = size,
                    Height = size,
                    Stretch = Stretch.Uniform,
                });
                ToolTip.SetTip(row.Children[^1], gift.Name);
            }
            else
            {
                row.Children.Add(new TextBlock
                {
                    Text = gift.Name,
                    FontSize = Math.Max(12, titleSize * 0.8),
                    Foreground = titleBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }
        }

        return row;
    }

    private void OnThemeChanged(AppTheme _) => Paint();

    private void Paint()
    {
        var showBg = _look.OverlayShowBackground;
        var opacity = OverlayLook.BackgroundOpacity(_look);
        _card.Background = showBg
            ? new SolidColorBrush(OverlayLook.Card(_look)) { Opacity = opacity }
            : Brushes.Transparent;
        _card.BorderBrush = showBg
            ? new SolidColorBrush(OverlayLook.Border(_look)) { Opacity = opacity }
            : Brushes.Transparent;
        Rebuild();
    }
}
