using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using HiveShock.Avalonia.Services;
using HiveShock.Avalonia.Themes;
using HiveShock.Configuration;

namespace HiveShock.Avalonia.Views.Overlays;

public sealed class GiftAlertOverlayWindow : Window
{
    private const double BaseWidth = 380;
    private const double BaseHeight = 220;
    private const double BaseMinWidth = 220;
    private const double BaseMinHeight = 120;

    private readonly WrapPanel _giftsPanel;
    private readonly DispatcherTimer _highlightTimer;
    private readonly Border _card;
    private readonly TextBlock _empty;
    private readonly TextBlock _title;
    private List<OverlayGiftChip> _chips = [];
    private OverlayGiftChip? _highlighted;
    private double _scale = 1.5;
    private IReadOnlyList<EditableGift> _lastGifts = [];
    private bool _userSized;
    private bool _applyingLayout;
    private UiPreferences _look = new();

    public GiftAlertOverlayWindow()
    {
        Title = "Regalos — HiveShock";
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

        var stack = new StackPanel();
        _title = new TextBlock
        {
            Text = "Regalos",
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        stack.Children.Add(_title);

        _giftsPanel = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
        _empty = new TextBlock
        {
            Text = "Marca “Mostrar en pantalla” en un regalo y pulsa Guardar.",
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
        };
        stack.Children.Add(_giftsPanel);
        _card.Child = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = stack,
        };
        Content = _card;

        _highlightTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _highlightTimer.Tick += (_, _) =>
        {
            _highlightTimer.Stop();
            ClearHighlight();
        };

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

    public void ApplyLook(UiPreferences prefs, double scale, IEnumerable<EditableGift>? gifts = null)
    {
        _look = prefs;
        ApplyScale(scale);
        if (gifts != null)
        {
            Apply(gifts);
        }
        else if (_lastGifts.Count > 0 || _giftsPanel.Children.Contains(_empty))
        {
            Apply(_lastGifts);
        }
        else
        {
            Paint();
        }
    }

    public void ApplyScale(double scale)
    {
        _applyingLayout = true;
        try
        {
            _scale = Math.Clamp(scale, 1.0, 3.0);
            var titleSize = OverlayLook.TitleSize(_look);
            _title.Text = OverlayLook.GiftsHeading(_look);
            _title.FontSize = titleSize;
            _title.IsVisible = _look.ShowGiftsOverlayTitle;
            _title.Margin = new Thickness(0, 0, 0, 10);
            _title.HorizontalAlignment = OverlayLook.AlignCenter(_look)
                ? HorizontalAlignment.Center
                : HorizontalAlignment.Left;
            _empty.FontSize = Math.Max(11, titleSize - 2);
            _empty.MaxWidth = 280 * _scale;
            _card.Padding = new Thickness(16 * _scale, 14 * _scale, 16 * _scale, 16 * _scale);
            _card.Margin = new Thickness(8 * _scale);
            _card.CornerRadius = new CornerRadius(10 * _scale);
            _giftsPanel.HorizontalAlignment = OverlayLook.AlignCenter(_look)
                ? HorizontalAlignment.Center
                : HorizontalAlignment.Left;

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

    public void Apply(IEnumerable<EditableGift> gifts)
    {
        var selected = gifts.Where(g => g.Overlay).ToList();
        _lastGifts = selected;
        _giftsPanel.Children.Clear();
        _chips = [];
        _highlighted = null;
        _highlightTimer.Stop();

        foreach (var gift in selected)
        {
            var chip = OverlayGiftChip.Create(gift, _scale, _look);
            _chips.Add(chip);
            _giftsPanel.Children.Add(chip.Root);
        }

        if (selected.Count == 0)
        {
            _giftsPanel.Children.Add(_empty);
        }

        Paint();
    }

    public void Highlight(string giftName, string? giftId)
    {
        OverlayGiftChip? hit = null;
        foreach (var chip in _chips)
        {
            if (chip.Matches(giftName, giftId))
            {
                hit = chip;
                break;
            }
        }

        if (hit == null)
        {
            return;
        }

        ClearHighlight();
        _highlighted = hit;
        hit.Paint(true, _look);
        _highlightTimer.Stop();
        _highlightTimer.Start();
    }

    private void OnThemeChanged(AppTheme _)
    {
        Paint();
        foreach (var chip in _chips)
        {
            chip.Paint(chip == _highlighted, _look);
        }
    }

    private void ClearHighlight()
    {
        if (_highlighted == null)
        {
            return;
        }

        _highlighted.Paint(false, _look);
        _highlighted = null;
    }

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
        _empty.Foreground = new SolidColorBrush(OverlayLook.Title(_look));
        _title.Foreground = new SolidColorBrush(OverlayLook.Title(_look));
    }

    private sealed class OverlayGiftChip
    {
        public Border Root { get; }
        public EditableGift Gift { get; }

        private OverlayGiftChip(Border root, EditableGift gift)
        {
            Root = root;
            Gift = gift;
        }

        public static OverlayGiftChip Create(EditableGift gift, double scale, UiPreferences look)
        {
            var nameSize = OverlayLook.GiftNameSize(look);
            var imageSize = Math.Max(24, nameSize * 2.1);
            var pad = 6 * Math.Clamp(scale, 1.0, 2.2);
            var image = new Image
            {
                Width = imageSize,
                Height = imageSize,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(0, 0, 8, 0),
                Source = GiftImageLoader.Load(gift.ResolvedImagePath),
            };

            var label = new TextBlock
            {
                Text = gift.OverlayLabel,
                FontSize = nameSize,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 180,
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(image);
            row.Children.Add(label);

            var border = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(pad + 2, pad, pad + 4, pad),
                Margin = new Thickness(4),
                Child = row,
            };

            var chip = new OverlayGiftChip(border, gift);
            chip.Paint(false, look);
            return chip;
        }

        public bool Matches(string giftName, string? giftId)
        {
            if (!string.IsNullOrWhiteSpace(giftId) &&
                string.Equals(Gift.Id, giftId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(Gift.Gift, giftName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return Gift.Also.Any(alias => string.Equals(alias, giftName, StringComparison.OrdinalIgnoreCase));
        }

        public void Paint(bool highlighted, UiPreferences look)
        {
            if (Root.Child is StackPanel row && row.Children.OfType<TextBlock>().FirstOrDefault() is { } label)
            {
                label.Foreground = new SolidColorBrush(OverlayLook.GiftText(look));
            }

            var card = OverlayLook.Card(look);
            var border = OverlayLook.Border(look);
            if (highlighted)
            {
                Root.BorderBrush = new SolidColorBrush(ThemeManager.OverlayAccent(ThemeManager.Current));
                Root.BorderThickness = new Thickness(2);
                Root.Background = new SolidColorBrush(ThemeManager.OverlayHighlight(ThemeManager.Current));
            }
            else if (look.OverlayShowBackground)
            {
                Root.BorderBrush = new SolidColorBrush(border);
                Root.BorderThickness = new Thickness(1);
                Root.Background = new SolidColorBrush(card);
            }
            else
            {
                Root.BorderBrush = Brushes.Transparent;
                Root.BorderThickness = new Thickness(0);
                Root.Background = Brushes.Transparent;
            }
        }
    }
}
