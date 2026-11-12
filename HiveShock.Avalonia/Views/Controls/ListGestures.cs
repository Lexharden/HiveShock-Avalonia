using System.Collections;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace HiveShock.Avalonia.Views.Controls;

/// <summary>
/// Gestos de escritorio para listas, declarados en XAML y sin código en cada página:
/// <list type="bullet">
/// <item><c>ctl:ListGestures.DeleteCommand</c>: Supr ejecuta el comando (que es quien pregunta antes de borrar).</item>
/// <item><c>ctl:ListGestures.ActivateCommand</c>: doble clic o Intro sobre un elemento (la acción principal).</item>
/// <item><c>ctl:ListGestures.Reorder</c>: arrastrar para cambiar el orden, y Alt+↑/↓ con el teclado.</item>
/// <item><c>ctl:ListGestures.ReorderFromHandle</c>: igual, pero solo desde un asa (<c>ctl:ListGestures.IsDragHandle</c>);
/// para filas con campos editables, donde arrastrar desde cualquier sitio robaría la selección de texto.</item>
/// </list>
/// Reordenar mueve el elemento en el <c>ItemsSource</c> (una <c>ObservableCollection</c>): el guardado automático
/// y quien escuche la colección se enteran solos. Con elementos ocultos (lista filtrada) se coloca junto al
/// elemento visible sobre el que se suelta.
/// </summary>
public static class ListGestures
{
    /// <summary>Distancia (px) que hay que mover el puntero para que una pulsación pase a ser un arrastre.</summary>
    private const double DragThreshold = 6;

    /// <summary>Franja (px) junto al borde de la lista que la desplaza mientras se arrastra.</summary>
    private const double AutoScrollEdge = 32;

    public static readonly AttachedProperty<ICommand?> DeleteCommandProperty =
        AvaloniaProperty.RegisterAttached<ItemsControl, ICommand?>("DeleteCommand", typeof(ListGestures));

    public static readonly AttachedProperty<ICommand?> ActivateCommandProperty =
        AvaloniaProperty.RegisterAttached<ItemsControl, ICommand?>("ActivateCommand", typeof(ListGestures));

    public static readonly AttachedProperty<bool> ReorderProperty =
        AvaloniaProperty.RegisterAttached<ItemsControl, bool>("Reorder", typeof(ListGestures));

    public static readonly AttachedProperty<bool> ReorderFromHandleProperty =
        AvaloniaProperty.RegisterAttached<ItemsControl, bool>("ReorderFromHandle", typeof(ListGestures));

    public static readonly AttachedProperty<bool> IsDragHandleProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsDragHandle", typeof(ListGestures));

    private static readonly ConditionalWeakTable<ItemsControl, DragState> States = new();

    static ListGestures()
    {
        DeleteCommandProperty.Changed.AddClassHandler<ItemsControl>((c, _) => Attach(c));
        ActivateCommandProperty.Changed.AddClassHandler<ItemsControl>((c, _) => Attach(c));
        ReorderProperty.Changed.AddClassHandler<ItemsControl>((c, _) => Attach(c));
        ReorderFromHandleProperty.Changed.AddClassHandler<ItemsControl>((c, _) => Attach(c));
    }

    public static ICommand? GetDeleteCommand(ItemsControl c) => c.GetValue(DeleteCommandProperty);
    public static void SetDeleteCommand(ItemsControl c, ICommand? value) => c.SetValue(DeleteCommandProperty, value);
    public static ICommand? GetActivateCommand(ItemsControl c) => c.GetValue(ActivateCommandProperty);
    public static void SetActivateCommand(ItemsControl c, ICommand? value) => c.SetValue(ActivateCommandProperty, value);
    public static bool GetReorder(ItemsControl c) => c.GetValue(ReorderProperty);
    public static void SetReorder(ItemsControl c, bool value) => c.SetValue(ReorderProperty, value);
    public static bool GetReorderFromHandle(ItemsControl c) => c.GetValue(ReorderFromHandleProperty);
    public static void SetReorderFromHandle(ItemsControl c, bool value) => c.SetValue(ReorderFromHandleProperty, value);
    public static bool GetIsDragHandle(Control c) => c.GetValue(IsDragHandleProperty);
    public static void SetIsDragHandle(Control c, bool value) => c.SetValue(IsDragHandleProperty, value);

    private static bool CanReorder(ItemsControl c) => GetReorder(c) || GetReorderFromHandle(c);

    /// <summary>Engancha los manejadores una sola vez por lista, sea cual sea la propiedad que se fije antes.</summary>
    private static void Attach(ItemsControl list)
    {
        if (States.TryGetValue(list, out _))
        {
            return;
        }

        States.Add(list, new DragState());
        // Túnel: la lista se queda con ↑/↓ e Intro para navegar antes de que lleguen aquí.
        list.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        list.AddHandler(InputElement.DoubleTappedEvent, OnDoubleTapped);
        // handledEventsToo: el ListBoxItem marca la pulsación como manejada al seleccionar.
        list.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        list.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        list.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        list.AddHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    // ------------------------------------------------------------------ teclado y doble clic

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not ItemsControl list || IsTextInput(e.Source as Visual, list))
        {
            return;
        }

        if (e.Key == Key.Escape && States.TryGetValue(list, out var drag) && drag.Dragging)
        {
            EndDrag(list, drag, commit: false);
            e.Handled = true;
            return;
        }

        var selected = (list as SelectingItemsControl)?.SelectedItem;
        if (selected is null)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Delete when e.KeyModifiers == KeyModifiers.None:
                e.Handled = Execute(GetDeleteCommand(list), selected);
                break;
            case Key.Enter when e.KeyModifiers == KeyModifiers.None:
                e.Handled = Execute(GetActivateCommand(list), selected);
                break;
            case Key.Up when e.KeyModifiers == KeyModifiers.Alt && CanReorder(list):
            case Key.Down when e.KeyModifiers == KeyModifiers.Alt && CanReorder(list):
                var from = IndexOf(list, selected);
                var to = from + (e.Key == Key.Up ? -1 : 1);
                if (from >= 0 && to >= 0 && to < Count(list))
                {
                    Move(list, selected, from, to);
                }

                e.Handled = true;
                break;
        }
    }

    private static void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is ItemsControl list && ContainerOf(list, e.Source as Visual) is { } container &&
            !IsInteractive(e.Source as Visual, container))
        {
            e.Handled = Execute(GetActivateCommand(list), list.ItemFromContainer(container));
        }
    }

    private static bool Execute(ICommand? command, object? parameter)
    {
        if (command is null || !command.CanExecute(parameter))
        {
            return false;
        }

        command.Execute(parameter);
        return true;
    }

    // ------------------------------------------------------------------ arrastrar para reordenar

    private sealed class DragState
    {
        public Control? Container;
        public object? Item;
        public Point Start;
        public bool Dragging;
        public double SavedOpacity;
        public Border? Indicator;
        public Point LastPosition;
        public DispatcherTimer? ScrollTimer;
        public int ScrollDirection;
        public ScrollViewer? Scroller;
        /// <summary>Elemento delante del que se insertará (null = al final).</summary>
        public object? InsertBefore;
        public bool HasTarget;
    }

    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not ItemsControl list || !CanReorder(list) || !States.TryGetValue(list, out var state) ||
            !e.GetCurrentPoint(list).Properties.IsLeftButtonPressed ||
            ContainerOf(list, e.Source as Visual) is not { } container)
        {
            return;
        }

        var source = e.Source as Visual;
        var allowed = GetReorderFromHandle(list) ? IsOnHandle(source, container) : !IsInteractive(source, container);
        if (!allowed)
        {
            return;
        }

        state.Container = container;
        state.Item = list.ItemFromContainer(container);
        state.Start = e.GetPosition(list);
        state.Dragging = false;
    }

    private static void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not ItemsControl list || !States.TryGetValue(list, out var state) || state.Container is null)
        {
            return;
        }

        var position = e.GetPosition(list);
        if (!state.Dragging)
        {
            if (!e.GetCurrentPoint(list).Properties.IsLeftButtonPressed)
            {
                Reset(state);
                return;
            }

            var delta = position - state.Start;
            if (Math.Abs(delta.Y) < DragThreshold && Math.Abs(delta.X) < DragThreshold)
            {
                return;
            }

            BeginDrag(list, state, e.Pointer);
        }

        state.LastPosition = position;
        UpdateTarget(list, state);
        UpdateAutoScroll(list, state, e);
        e.Handled = true;
    }

    private static void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not ItemsControl list || !States.TryGetValue(list, out var state))
        {
            return;
        }

        if (state.Dragging)
        {
            EndDrag(list, state, commit: true);
            e.Pointer.Capture(null);
            e.Handled = true;
        }
        else
        {
            Reset(state);
        }
    }

    private static void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (sender is ItemsControl list && States.TryGetValue(list, out var state) && state.Dragging)
        {
            EndDrag(list, state, commit: false);
        }
    }

    private static void BeginDrag(ItemsControl list, DragState state, IPointer pointer)
    {
        state.Dragging = true;
        pointer.Capture(list);
        state.SavedOpacity = state.Container!.Opacity;
        state.Container.Opacity = 0.45;
        state.Scroller = list.FindDescendantOfType<ScrollViewer>() ?? list.FindAncestorOfType<ScrollViewer>();

        var brush = list.TryFindResource("GoldTextBrush", list.ActualThemeVariant, out var res) && res is IBrush b
            ? b
            : Brushes.Goldenrod;
        state.Indicator = new Border
        {
            Height = 3,
            CornerRadius = new CornerRadius(1.5),
            Background = brush,
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsVisible = false,
        };
        if (AdornerLayer.GetAdornerLayer(list) is { } layer)
        {
            AdornerLayer.SetAdornedElement(state.Indicator, list);
            layer.Children.Add(state.Indicator);
        }

        list.Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
    }

    private static void EndDrag(ItemsControl list, DragState state, bool commit)
    {
        state.ScrollTimer?.Stop();
        if (state.Indicator is { } indicator && AdornerLayer.GetAdornerLayer(list) is { } layer)
        {
            layer.Children.Remove(indicator);
        }

        if (state.Container is { } container)
        {
            container.Opacity = state.SavedOpacity;
        }

        list.Cursor = null;
        var item = state.Item;
        var hasTarget = state.HasTarget;
        var before = state.InsertBefore;
        Reset(state);

        if (!commit || !hasTarget || item is null)
        {
            return;
        }

        var from = IndexOf(list, item);
        var insert = before is null ? Count(list) : IndexOf(list, before);
        if (from < 0 || insert < 0)
        {
            return;
        }

        var to = insert > from ? insert - 1 : insert;
        if (to != from)
        {
            Move(list, item, from, to);
        }
    }

    private static void Reset(DragState state)
    {
        state.ScrollTimer?.Stop();
        state.Container = null;
        state.Item = null;
        state.Dragging = false;
        state.Indicator = null;
        state.Scroller = null;
        state.InsertBefore = null;
        state.HasTarget = false;
    }

    /// <summary>Busca el hueco bajo el puntero: delante del primer elemento visible cuya mitad queda por debajo.</summary>
    private static void UpdateTarget(ItemsControl list, DragState state)
    {
        var y = state.LastPosition.Y;
        Control? last = null;
        double lineY = 0;
        object? before = null;
        var found = false;

        foreach (var (container, top, height) in VisibleContainers(list))
        {
            if (y < top + height / 2)
            {
                before = list.ItemFromContainer(container);
                lineY = top - 3;
                found = true;
                break;
            }

            last = container;
            lineY = top + height + 1;
        }

        if (!found && last is null)
        {
            state.HasTarget = false;
            if (state.Indicator is { } hidden)
            {
                hidden.IsVisible = false;
            }

            return;
        }

        state.HasTarget = true;
        state.InsertBefore = found ? before : null;
        if (state.Indicator is { } indicator)
        {
            indicator.IsVisible = true;
            indicator.Margin = new Thickness(6, Math.Clamp(lineY, 0, Math.Max(0, list.Bounds.Height - 3)), 6, 0);
        }
    }

    private static IEnumerable<(Control Container, double Top, double Height)> VisibleContainers(ItemsControl list)
    {
        var rows = new List<(Control, double, double)>();
        foreach (var container in list.GetRealizedContainers())
        {
            if (!container.IsVisible || container.Bounds.Height <= 0 ||
                container.TranslatePoint(default, list) is not { } p)
            {
                continue;
            }

            rows.Add((container, p.Y, container.Bounds.Height));
        }

        rows.Sort(static (a, b) => a.Item2.CompareTo(b.Item2));
        return rows;
    }

    /// <summary>Cerca del borde de la zona visible, desplaza mientras el puntero siga ahí.</summary>
    private static void UpdateAutoScroll(ItemsControl list, DragState state, PointerEventArgs e)
    {
        if (state.Scroller is not { } scroller)
        {
            return;
        }

        var y = e.GetPosition(scroller).Y;
        var speed = y < AutoScrollEdge ? -1 : y > scroller.Bounds.Height - AutoScrollEdge ? 1 : 0;
        if (speed == 0)
        {
            state.ScrollTimer?.Stop();
            return;
        }

        state.ScrollTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(30), DispatcherPriority.Input, (_, _) =>
        {
            if (!state.Dragging || state.Scroller is not { } sv)
            {
                state.ScrollTimer?.Stop();
                return;
            }

            var maxY = Math.Max(0, sv.Extent.Height - sv.Viewport.Height);
            sv.Offset = sv.Offset.WithY(Math.Clamp(sv.Offset.Y + state.ScrollDirection * 14, 0, maxY));
            UpdateTarget(list, state);
        });
        state.ScrollDirection = speed;
        if (!state.ScrollTimer.IsEnabled)
        {
            state.ScrollTimer.Start();
        }
    }

    // ------------------------------------------------------------------ utilidades

    private static void Move(ItemsControl list, object item, int from, int to)
    {
        if (list.ItemsSource is not IList source)
        {
            return;
        }

        // ObservableCollection<T>.Move avisa con una sola notificación «Move» (sin quitar y volver a poner).
        var move = source.GetType().GetMethod("Move", [typeof(int), typeof(int)]);
        if (move is not null)
        {
            move.Invoke(source, [from, to]);
        }
        else
        {
            source.RemoveAt(from);
            source.Insert(to, item);
        }

        if (list is SelectingItemsControl selecting)
        {
            selecting.SelectedItem = item;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (list.ContainerFromItem(item) is { } container)
            {
                container.BringIntoView();
                if (list is SelectingItemsControl)
                {
                    container.Focus();
                }
            }
        }, DispatcherPriority.Loaded);
    }

    private static int IndexOf(ItemsControl list, object item) => list.ItemsSource is IList source ? source.IndexOf(item) : -1;

    private static int Count(ItemsControl list) => list.ItemsSource is IList source ? source.Count : 0;

    private static Control? ContainerOf(ItemsControl list, Visual? source)
    {
        for (var v = source; v is not null && !ReferenceEquals(v, list); v = v.GetVisualParent())
        {
            if (v is Control c && list.IndexFromContainer(c) >= 0)
            {
                return c;
            }
        }

        return null;
    }

    /// <summary>Pulsar en un campo, botón o desplegable de la fila es para usarlo, no para arrastrar ni abrir.</summary>
    private static bool IsInteractive(Visual? source, Visual stopAt)
    {
        for (var v = source; v is not null && !ReferenceEquals(v, stopAt); v = v.GetVisualParent())
        {
            if (v is TextBox or Button or ComboBox or Slider or NumericUpDown or ToggleSwitch or ScrollBar)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTextInput(Visual? source, Visual stopAt)
    {
        for (var v = source; v is not null && !ReferenceEquals(v, stopAt); v = v.GetVisualParent())
        {
            if (v is TextBox)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsOnHandle(Visual? source, Visual stopAt)
    {
        for (var v = source; v is not null && !ReferenceEquals(v, stopAt); v = v.GetVisualParent())
        {
            if (v is Control c && GetIsDragHandle(c))
            {
                return true;
            }
        }

        return false;
    }
}
