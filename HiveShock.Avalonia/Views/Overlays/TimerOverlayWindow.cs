using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using HiveShock.Avalonia.Services;
using HiveShock.Avalonia.Themes;
using HiveShock.Zeldathon;

namespace HiveShock.Avalonia.Views.Overlays;

/// <summary>Cronómetro en pantalla para OBS: reloj oficial de Zeldathon o cronómetro local.</summary>
public sealed class TimerOverlayWindow : Window
{
    private const double BaseWidth = 300;
    private const double BaseHeight = 190;
    private const double BaseMinWidth = 200;
    private const double BaseMinHeight = 110;

    private readonly TextBlock _titleBlock;
    private readonly TextBlock _timeBlock;
    private readonly TextBlock _statusBlock;
    private readonly TextBlock _resetBlock;
    private readonly ProgressBar _bar;
    private readonly Border _card;
    private readonly StackPanel _stack;
    private readonly DispatcherTimer _tick;
    private TimerSource? _source;
    private double _scale = 1.5;
    private bool _userSized;
    private bool _applyingLayout;
    private UiPreferences _look = new();

    public TimerOverlayWindow()
    {
        Title = "Cronómetro — HiveShock";
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
        _titleBlock = new TextBlock { FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        _timeBlock = new TextBlock
        {
            FontWeight = FontWeight.Bold,
            // Ancho fijo por dígito: el reloj no "baila" al cambiar cada segundo.
            FontFamily = new FontFamily("Consolas, Menlo, Cascadia Mono, DejaVu Sans Mono, monospace"),
        };
        _statusBlock = new TextBlock { FontWeight = FontWeight.SemiBold };
        _resetBlock = new TextBlock { Opacity = 0.8 };
        _bar = new ProgressBar { Minimum = 0, Maximum = 1, Height = 8, Margin = new Thickness(0, 8, 0, 0) };
        _stack.Children.Add(_titleBlock);
        _stack.Children.Add(_timeBlock);
        _stack.Children.Add(_statusBlock);
        _stack.Children.Add(_resetBlock);
        _stack.Children.Add(_bar);
        _card.Child = _stack;
        Content = _card;

        _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _tick.Tick += (_, _) => Refresh();
        Opened += (_, _) => _tick.Start();
        ThemeManager.ThemeChanged += OnThemeChanged;
        Closed += (_, _) =>
        {
            _tick.Stop();
            ThemeManager.ThemeChanged -= OnThemeChanged;
        };
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

    public void Bind(TimerSource source)
    {
        _source = source;
        Refresh();
    }

    public void ApplyLook(UiPreferences prefs, double scale)
    {
        _look = prefs;
        ApplyScale(scale);
        Refresh();
    }

    public void ApplyScale(double scale)
    {
        _applyingLayout = true;
        try
        {
            _scale = Math.Clamp(scale, 1.0, 3.0);
            var t = _look.Timer;
            var timeSize = t.ResolveTimeSize();
            var titleSize = OverlayLook.TitleSize(_look);
            _titleBlock.FontSize = titleSize;
            _timeBlock.FontSize = timeSize;
            _statusBlock.FontSize = Math.Max(11, titleSize * 0.85);
            _resetBlock.FontSize = Math.Max(10, titleSize * 0.75);
            _titleBlock.Margin = new Thickness(0, 0, 0, 4);
            _statusBlock.Margin = new Thickness(0, 4, 0, 0);
            _card.Padding = new Thickness(22 * _scale, 14 * _scale, 22 * _scale, 16 * _scale);
            _card.Margin = new Thickness(8 * _scale);
            _card.CornerRadius = new CornerRadius(10 * _scale);

            var align = OverlayLook.AlignCenter(_look) ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            _titleBlock.HorizontalAlignment = align;
            _timeBlock.HorizontalAlignment = align;
            _statusBlock.HorizontalAlignment = align;
            _resetBlock.HorizontalAlignment = align;
            _stack.HorizontalAlignment = HorizontalAlignment.Stretch;

            MinWidth = BaseMinWidth * _scale;
            MinHeight = BaseMinHeight * _scale;

            // "00:00:00" son 8 caracteres de ~0,62 em: el ancho sigue al tamaño del número.
            var contentWidth = timeSize * 0.62 * 8 + 44 * _scale + 16 * _scale;
            if (!_userSized)
            {
                Width = Math.Max(BaseWidth * _scale * 0.7, contentWidth);
                Height = Math.Max(BaseMinHeight * _scale, timeSize + titleSize * 2.6 + 70 * _scale * 0.6);
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

    private void OnThemeChanged(AppTheme _) => Refresh();

    private void Refresh()
    {
        Paint();
        if (_source == null)
        {
            return;
        }

        var shown = _source.Current();
        var t = _look.Timer;
        var color = _source.ToneColor(shown.Tone);
        _titleBlock.Text = shown.Label;
        _timeBlock.Text = shown.TimeText;
        _timeBlock.Foreground = new SolidColorBrush(color);
        _statusBlock.Text = shown.StatusText;
        _statusBlock.Foreground = new SolidColorBrush(color);
        _statusBlock.IsVisible = t.ShowStatus;
        _resetBlock.Text = shown.ResetText;
        _resetBlock.IsVisible = t.ShowReset && shown.ResetText.Length > 0;
        _bar.IsVisible = t.ShowBar && (!t.IsLocal || t.LocalCountdown);
        _bar.Value = shown.UsedFraction;
        _bar.Foreground = new SolidColorBrush(color);
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
        _titleBlock.Foreground = new SolidColorBrush(OverlayLook.Title(_look));
        _resetBlock.Foreground = new SolidColorBrush(OverlayLook.Title(_look));
    }
}
