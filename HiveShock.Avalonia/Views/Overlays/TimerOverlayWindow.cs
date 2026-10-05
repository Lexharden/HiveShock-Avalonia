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

/// <summary>Cronómetro en pantalla para OBS: reloj oficial de Zeldatón o cronómetro local.</summary>
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
    private readonly DispatcherTimer _frame;
    private readonly Border _deltaRow;
    private readonly TextBlock _deltaText;
    private readonly ScaleTransform _deltaScale = new(1, 1);
    private readonly TranslateTransform _deltaShift = new();
    private Color _baseTimeColor;
    private long _deltaVersion = -1;
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
        _deltaText = new TextBlock
        {
            FontWeight = FontWeight.Bold,
            Opacity = 0,
            FontFamily = new FontFamily("Consolas, Menlo, Cascadia Mono, DejaVu Sans Mono, monospace"),
            RenderTransformOrigin = RelativePoint.Center,
            RenderTransform = new TransformGroup { Children = { _deltaScale, _deltaShift } },
            VerticalAlignment = VerticalAlignment.Center,
        };
        // Fila reservada bajo el reloj: así el rótulo aparece sin mover nada de lo demás.
        _deltaRow = new Border { Child = _deltaText, IsVisible = false };
        _stack.Children.Add(_titleBlock);
        _stack.Children.Add(_timeBlock);
        _stack.Children.Add(_deltaRow);
        _stack.Children.Add(_statusBlock);
        _stack.Children.Add(_resetBlock);
        _stack.Children.Add(_bar);
        _card.Child = _stack;
        Content = _card;

        _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _tick.Tick += (_, _) => Refresh();
        // Solo corre mientras hay un rótulo en pantalla: ~30 fotogramas por segundo para que sea fluido.
        _frame = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _frame.Tick += (_, _) => RenderDelta();
        Opened += (_, _) => _tick.Start();
        ThemeManager.ThemeChanged += OnThemeChanged;
        Closed += (_, _) =>
        {
            _tick.Stop();
            _frame.Stop();
            Unsubscribe();
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
        Unsubscribe();
        _source = source;
        source.Deltas.Pushed += OnDeltaPushed;
        Refresh();
    }

    private void Unsubscribe()
    {
        if (_source != null)
        {
            _source.Deltas.Pushed -= OnDeltaPushed;
        }
    }

    /// <summary>Llega desde el hilo del servidor: se arranca la animación en el hilo de la interfaz.</summary>
    private void OnDeltaPushed() => Dispatcher.UIThread.Post(() =>
    {
        if (ShowDeltaRow && !_frame.IsEnabled)
        {
            _frame.Start();
        }

        RenderDelta();
    });

    /// <summary>Con el reloj oficial y la opción activa se reserva la fila del rótulo.</summary>
    private bool ShowDeltaRow => _look.Timer.ShowDelta && !_look.Timer.IsLocal;

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
            var deltaSize = Math.Max(16, timeSize * 0.36);
            var deltaRowHeight = ShowDeltaRow ? deltaSize * 1.5 : 0;
            _deltaText.FontSize = deltaSize;
            _deltaRow.Height = deltaRowHeight;
            _deltaRow.IsVisible = ShowDeltaRow;
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
            _deltaText.HorizontalAlignment = align;
            _stack.HorizontalAlignment = HorizontalAlignment.Stretch;

            MinWidth = BaseMinWidth * _scale;
            MinHeight = BaseMinHeight * _scale;

            // "00:00:00" son 8 caracteres de ~0,62 em: el ancho sigue al tamaño del número.
            var contentWidth = timeSize * 0.62 * 8 + 44 * _scale + 16 * _scale;
            if (!_userSized)
            {
                Width = Math.Max(BaseWidth * _scale * 0.7, contentWidth);
                Height = Math.Max(BaseMinHeight * _scale, timeSize + titleSize * 2.6 + 70 * _scale * 0.6 + deltaRowHeight);
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
        _baseTimeColor = color;
        _timeBlock.Foreground = new SolidColorBrush(FlashColor(color));
        _statusBlock.Text = shown.StatusText;
        _statusBlock.Foreground = new SolidColorBrush(color);
        _statusBlock.IsVisible = t.ShowStatus;
        _resetBlock.Text = shown.ResetText;
        _resetBlock.IsVisible = t.ShowReset && shown.ResetText.Length > 0;
        _bar.IsVisible = t.ShowBar && (!t.IsLocal || t.LocalCountdown);
        _bar.Value = shown.UsedFraction;
        _bar.Foreground = new SolidColorBrush(color);
    }

    private const double PopInMs = 180;
    private const double FadeOutMs = 800;
    private const double FlashMs = 700;

    /// <summary>
    /// Un cuadro del rótulo «+1:30» / «−0:30»: entra con un pequeño rebote, se queda y se desvanece subiendo.
    /// Cada donación nueva en la misma ráfaga lo actualiza y reinicia el rebote. Todo se calcula del tiempo
    /// transcurrido desde la última donación, así que no depende de cuántos cuadros se pinten.
    /// </summary>
    private void RenderDelta()
    {
        var burst = ShowDeltaRow ? _source?.Deltas.Current() : null;
        if (_source == null || burst == null || burst.Seconds == 0)
        {
            _frame.Stop();
            _deltaText.Opacity = 0;
            _deltaVersion = -1;
            _timeBlock.Foreground = new SolidColorBrush(_baseTimeColor);
            return;
        }

        if (burst.Version != _deltaVersion)
        {
            _deltaVersion = burst.Version;
            _deltaText.Inlines ??= new global::Avalonia.Controls.Documents.InlineCollection();
            _deltaText.Inlines.Clear();
            _deltaText.Inlines.Add(new global::Avalonia.Controls.Documents.Run(TimerDisplayBuilder.FormatDelta(burst.Seconds)));
            var extra = (burst.Count > 1 ? $"  ×{burst.Count}" : "") + (burst.Limited ? "  tope" : "");
            if (extra.Length > 0)
            {
                _deltaText.Inlines.Add(new global::Avalonia.Controls.Documents.Run(extra)
                {
                    FontSize = Math.Max(11, _deltaText.FontSize * 0.5),
                    FontWeight = FontWeight.SemiBold,
                });
            }

            _deltaText.Foreground = new SolidColorBrush(_source.DeltaColor(burst.Seconds));
        }

        var age = burst.AgeMs;
        var pop = Math.Clamp(age / PopInMs, 0, 1);
        var eased = 1 - Math.Pow(1 - pop, 3);
        var fadeStart = TimeDeltaFeed.HoldFor.TotalMilliseconds - FadeOutMs;
        var fade = Math.Clamp((age - fadeStart) / FadeOutMs, 0, 1);
        _deltaText.Opacity = eased * (1 - fade);
        var scale = 1 + 0.25 * (1 - eased);
        _deltaScale.ScaleX = scale;
        _deltaScale.ScaleY = scale;
        _deltaShift.Y = (1 - eased) * 8 - fade * 12;
        _timeBlock.Foreground = new SolidColorBrush(FlashColor(_baseTimeColor));
    }

    /// <summary>Un destello del color del cambio sobre el número cuando el reloj salta; vuelve solo al normal.</summary>
    private Color FlashColor(Color baseColor)
    {
        var burst = ShowDeltaRow ? _source?.Deltas.Current() : null;
        if (_source == null || burst == null || burst.Seconds == 0 || burst.AgeMs >= FlashMs)
        {
            return baseColor;
        }

        var flash = _source.DeltaColor(burst.Seconds);
        var t = burst.AgeMs / FlashMs;
        byte Mix(byte a, byte b) => (byte)Math.Round(a + (b - a) * t);
        return Color.FromRgb(Mix(flash.R, baseColor.R), Mix(flash.G, baseColor.G), Mix(flash.B, baseColor.B));
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
