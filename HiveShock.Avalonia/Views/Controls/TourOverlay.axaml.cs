using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using HiveShock.Avalonia.ViewModels;

namespace HiveShock.Avalonia.Views.Controls;

public partial class TourOverlay : UserControl
{
    private const double Gap = 16;
    private const double EdgeMargin = 16;
    private const double HolePadding = 6;
    /// <summary>Intentos (×100 ms) para que aparezca el objetivo tras navegar antes de mostrar la tarjeta centrada.</summary>
    private const int MaxAttempts = 15;

    private readonly DispatcherTimer _tracker = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private TutorialViewModel? _vm;
    private TutorialStep? _step;
    private Control? _target;
    private int _attempts;

    public TourOverlay()
    {
        InitializeComponent();
        _tracker.Tick += (_, _) => Track();
        KeyDown += OnKeyDown;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm != null)
        {
            _vm.StepEntered -= OnStepEntered;
            _vm.PropertyChanged -= OnVmChanged;
        }

        _vm = DataContext as TutorialViewModel;
        if (_vm != null)
        {
            _vm.StepEntered += OnStepEntered;
            _vm.PropertyChanged += OnVmChanged;
        }
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TutorialViewModel.IsOpen) || _vm == null)
        {
            return;
        }

        if (_vm.IsOpen)
        {
            _tracker.Start();
        }
        else
        {
            _tracker.Stop();
            _step = null;
            _target = null;
            Spot.MoveTo(null, animate: false);
        }
    }

    private void OnStepEntered(TutorialStep step)
    {
        _step = step;
        _target = null;
        _attempts = 0;
        // Se oculta la tarjeta hasta saber dónde va: así no salta por la pantalla mientras navega.
        Card.Opacity = 0;
        Dispatcher.UIThread.Post(() =>
        {
            Track();
            NextButton.Focus();
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// Busca el objetivo (puede tardar: la página se crea al navegar), lo resalta y coloca la tarjeta.
    /// Se repite cada 100 ms mientras el tutorial está abierto para seguir cambios de tamaño, scroll o del menú.
    /// </summary>
    private void Track()
    {
        if (_step == null || !IsVisible || Bounds.Width <= 0)
        {
            return;
        }

        var top = TopLevel.GetTopLevel(this);
        if (_step.SelectTab is { } tabKey && Tour.Find(tabKey, top) is TabItem tab && !tab.IsSelected &&
            tab.FindLogicalAncestorOfType<TabControl>() is { } tabs)
        {
            tabs.SelectedItem = tab;
            return; // el contenido de la pestaña aparece en la siguiente pasada
        }

        if (_step.Target is not { } key)
        {
            Place(null);
            return;
        }

        if (_target == null || !_target.IsEffectivelyVisible || TopLevel.GetTopLevel(_target) == null)
        {
            _target = Tour.Find(key, top);
            if (_target == null)
            {
                if (++_attempts >= MaxAttempts)
                {
                    Place(null); // no está (p. ej. sin regalos): se explica igual, con la tarjeta centrada
                }

                return;
            }

            _target.BringIntoView();
        }

        if (_target.TranslatePoint(default, this) is not { } origin)
        {
            return;
        }

        var hole = new Rect(origin, _target.Bounds.Size).Inflate(HolePadding);
        Place(hole.Intersect(new Rect(Bounds.Size)));
    }

    private void Place(Rect? hole)
    {
        Spot.MoveTo(hole, animate: true);

        var card = Card;
        card.Measure(new Size(card.Width, double.PositiveInfinity));
        var cw = card.Width;
        var ch = card.DesiredSize.Height;
        var w = Bounds.Width;
        var h = Bounds.Height;

        double x, y;
        if (hole is not { } r || r.Width <= 0)
        {
            x = (w - cw) / 2;
            y = (h - ch) / 2;
        }
        else if (w - r.Right >= cw + Gap + EdgeMargin)
        {
            x = r.Right + Gap;
            y = r.Top;
        }
        else if (r.Left >= cw + Gap + EdgeMargin)
        {
            x = r.Left - Gap - cw;
            y = r.Top;
        }
        else if (h - r.Bottom >= ch + Gap + EdgeMargin)
        {
            x = r.Left;
            y = r.Bottom + Gap;
        }
        else if (r.Top >= ch + Gap + EdgeMargin)
        {
            x = r.Left;
            y = r.Top - Gap - ch;
        }
        else
        {
            // El objetivo ocupa casi todo: la tarjeta va abajo a la derecha, encima del resaltado.
            x = w - cw - EdgeMargin * 2;
            y = h - ch - EdgeMargin * 2;
        }

        Canvas.SetLeft(card, Math.Clamp(x, EdgeMargin, Math.Max(EdgeMargin, w - cw - EdgeMargin)));
        Canvas.SetTop(card, Math.Clamp(y, EdgeMargin, Math.Max(EdgeMargin, h - ch - EdgeMargin)));
        card.Opacity = 1;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is not { IsOpen: true })
        {
            return;
        }

        // Intro/Espacio ya pulsan el botón con el foco; aquí solo flechas y Esc.
        switch (e.Key)
        {
            case Key.Right:
                _vm.NextCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Left:
                _vm.BackCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape:
                _vm.FinishCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }
}

/// <summary>
/// Oscurece todo salvo un rectángulo redondeado (el «foco») con un borde dorado.
/// El foco se desliza entre pasos con una animación corta.
/// </summary>
public sealed class TourSpotlight : Control
{
    private const double Radius = 10;
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(240);

    private readonly DispatcherTimer _animation = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private Rect? _from;
    private Rect? _to;
    private Rect? _current;
    private DateTime _started;

    public TourSpotlight()
    {
        IsHitTestVisible = false;
        _animation.Tick += (_, _) => Step();
    }

    public void MoveTo(Rect? target, bool animate)
    {
        if (Nullable.Equals(target, _to))
        {
            return;
        }

        _to = target;
        if (!animate || target == null)
        {
            _animation.Stop();
            _current = target;
            InvalidateVisual();
            return;
        }

        // Desde el foco actual; si no había, se cierra desde toda la ventana hacia el objetivo.
        _from = _current ?? new Rect(Bounds.Size);
        _started = DateTime.UtcNow;
        _animation.Start();
    }

    private void Step()
    {
        if (_from is not { } a || _to is not { } b)
        {
            _animation.Stop();
            return;
        }

        var t = Math.Min(1, (DateTime.UtcNow - _started) / Duration);
        var e = 1 - Math.Pow(1 - t, 3); // ease-out cúbico
        _current = new Rect(
            a.X + (b.X - a.X) * e,
            a.Y + (b.Y - a.Y) * e,
            a.Width + (b.Width - a.Width) * e,
            a.Height + (b.Height - a.Height) * e);
        if (t >= 1)
        {
            _animation.Stop();
        }

        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var full = new Rect(Bounds.Size);
        var dim = new SolidColorBrush(Color.FromArgb(0xB8, 0x05, 0x07, 0x0C));
        if (_current is not { } hole || hole.Width <= 0)
        {
            context.FillRectangle(dim, full);
            return;
        }

        var shape = new CombinedGeometry(
            GeometryCombineMode.Exclude,
            new RectangleGeometry(full),
            new RectangleGeometry(hole) { RadiusX = Radius, RadiusY = Radius });
        context.DrawGeometry(dim, null, shape);

        var gold = Color.Parse("#EBA00A");
        context.DrawRectangle(null, new Pen(new SolidColorBrush(gold, 0.25), 8), hole, Radius, Radius);
        context.DrawRectangle(null, new Pen(new SolidColorBrush(gold), 2), hole, Radius, Radius);
    }
}
