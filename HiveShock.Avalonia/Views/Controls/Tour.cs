using Avalonia;
using Avalonia.Controls;

namespace HiveShock.Avalonia.Views.Controls;

/// <summary>
/// Marca un control como objetivo del tutorial: <c>ctl:Tour.Key="tiktok.gifts.list"</c>.
/// El registro guarda referencias débiles: las páginas se recrean al navegar y no deben quedar vivas por esto.
/// Si hay varios controles con la misma clave (p. ej. la tarjeta de conectar con el menú abierto y el botón
/// con el menú plegado) se usa el que esté visible en esa ventana.
/// </summary>
public static class Tour
{
    private static readonly List<WeakReference<Control>> Registered = [];

    public static readonly AttachedProperty<string?> KeyProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("Key", typeof(Tour));

    static Tour()
    {
        KeyProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            if (e.NewValue is string { Length: > 0 })
            {
                lock (Registered)
                {
                    Registered.Add(new WeakReference<Control>(control));
                }
            }
        });
    }

    public static string? GetKey(Control control) => control.GetValue(KeyProperty);

    public static void SetKey(Control control, string? value) => control.SetValue(KeyProperty, value);

    /// <summary>El control visible con esa clave dentro de <paramref name="root"/> (misma ventana), o null.</summary>
    public static Control? Find(string key, TopLevel? root)
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

                if (found == null &&
                    GetKey(control) == key &&
                    control.IsEffectivelyVisible &&
                    control.Bounds is { Width: > 0, Height: > 0 } &&
                    TopLevel.GetTopLevel(control) is { } top &&
                    (root == null || ReferenceEquals(top, root)))
                {
                    found = control;
                }
            }

            return found;
        }
    }
}
