using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace HiveShock.Avalonia.Views.Controls;

/// <summary>
/// Atajo de teclado «de página»: <c>ctl:Hotkey.Gesture="Ctrl+N"</c> en un botón o buscador.
/// Al pulsarlo en la ventana actúa sobre el control con ese atajo que esté a la vista (la pestaña abierta):
/// un buscador recibe el foco con el texto seleccionado, un botón ejecuta su comando o abre su menú.
/// Así cada lista tiene su Ctrl+N / Ctrl+F sin código por página y sin chocar con las de otras pestañas.
/// </summary>
public static class Hotkey
{
    private static readonly List<WeakReference<Control>> Registered = [];
    private static readonly ConditionalWeakTable<TopLevel, object> Hooked = new();

    public static readonly AttachedProperty<string?> GestureProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("Gesture", typeof(Hotkey));

    static Hotkey()
    {
        GestureProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            if (e.NewValue is not string { Length: > 0 })
            {
                return;
            }

            lock (Registered)
            {
                Registered.Add(new WeakReference<Control>(control));
            }

            control.AttachedToVisualTree += (_, _) => Hook(TopLevel.GetTopLevel(control));
            Hook(TopLevel.GetTopLevel(control));
        });
    }

    public static string? GetGesture(Control control) => control.GetValue(GestureProperty);

    public static void SetGesture(Control control, string? value) => control.SetValue(GestureProperty, value);

    private static void Hook(TopLevel? top)
    {
        if (top is null || Hooked.TryGetValue(top, out _))
        {
            return;
        }

        Hooked.Add(top, new object());
        // Túnel: el atajo funciona aunque el foco esté en un campo de texto (Ctrl+F desde cualquier sitio).
        top.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TopLevel top || e.KeyModifiers == KeyModifiers.None || Find(top, e) is not { } target)
        {
            return;
        }

        e.Handled = Activate(target);
    }

    private static Control? Find(TopLevel top, KeyEventArgs e)
    {
        lock (Registered)
        {
            Control? found = null;
            for (var i = Registered.Count - 1; i >= 0; i--)
            {
                if (!Registered[i].TryGetTarget(out var control))
                {
                    Registered.RemoveAt(i);
                    continue;
                }

                if (found is null &&
                    control.IsEffectivelyVisible && control.IsEffectivelyEnabled &&
                    ReferenceEquals(TopLevel.GetTopLevel(control), top) &&
                    Matches(GetGesture(control), e))
                {
                    found = control;
                }
            }

            return found;
        }
    }

    private static bool Matches(string? gesture, KeyEventArgs e)
    {
        try
        {
            return gesture is { Length: > 0 } && KeyGesture.Parse(gesture).Matches(e);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool Activate(Control target)
    {
        switch (target)
        {
            case TextBox box:
                box.Focus(NavigationMethod.Tab);
                box.SelectAll();
                return true;
            case Button { Command: { } command } button when command.CanExecute(button.CommandParameter):
                command.Execute(button.CommandParameter);
                return true;
            case Button { Flyout: { } flyout } button:
                flyout.ShowAt(button);
                return true;
            default:
                return target.Focus();
        }
    }
}
