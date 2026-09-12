using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using HiveShock.Avalonia.Services;
using HiveShock.Avalonia.Themes;
using HiveShock.Hosting;

namespace HiveShock.Avalonia.Views.Overlays;

public sealed class CounterOverlayWindow : Window
{
    private const double BaseWidth = 260;
    private const double BaseHeight = 168;
    private const double BaseMinWidth = 180;
    private const double BaseMinHeight = 128;

    private readonly TextBlock _titleBlock;
    private readonly TextBlock _valueBlock;
    private readonly Border _card;
    private readonly StackPanel _stack;
    private double _scale = 1.5;
    private bool _userSized;
    private bool _applyingLayout;
    private UiPreferences _look = new();

    public CounterOverlayWindow()
    {
        Title = "Contador — HiveShock";
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
        _titleBlock = new TextBlock
        {
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _valueBlock = new TextBlock
        {
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _stack.Children.Add(_titleBlock);
        _stack.Children.Add(_valueBlock);
        _card.Child = _stack;
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
        ApplyScale(scale);
        Paint();
    }

    public void ApplyScale(double scale)
    {
        _applyingLayout = true;
        try
        {
            _scale = Math.Clamp(scale, 1.0, 3.0);
            var titleSize = OverlayLook.TitleSize(_look);
            var numberSize = OverlayLook.NumberSize(_look);
            _titleBlock.FontSize = titleSize;
            _valueBlock.FontSize = numberSize;
            _titleBlock.Margin = new Thickness(0, 0, 0, 6);
            _card.Padding = new Thickness(22 * _scale, 16 * _scale, 22 * _scale, 18 * _scale);
            _card.Margin = new Thickness(8 * _scale);
            _card.CornerRadius = new CornerRadius(10 * _scale);

            var align = OverlayLook.AlignCenter(_look) ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            _titleBlock.HorizontalAlignment = align;
            _valueBlock.HorizontalAlignment = align;
            _stack.HorizontalAlignment = align;

            MinWidth = BaseMinWidth * _scale;
            MinHeight = BaseMinHeight * _scale;

            if (!_userSized)
            {
                Width = Math.Max(BaseWidth * _scale, numberSize * 2.2);
                Height = Math.Max(BaseHeight * _scale, numberSize + titleSize + 80);
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

    public void Apply(DeathCounter counter, string? title = null)
    {
        _titleBlock.Text = string.IsNullOrWhiteSpace(title) ? counter.Title : title;
        _valueBlock.Text = counter.Value.ToString();
        Paint();
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
        _titleBlock.Foreground = new SolidColorBrush(OverlayLook.Title(_look));
        _valueBlock.Foreground = new SolidColorBrush(OverlayLook.Number(_look));
    }
}
