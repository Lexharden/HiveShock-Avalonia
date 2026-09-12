using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace HiveShock.Avalonia.Services;

/// <summary>Asigna owner de diálogo solo cuando la ventana principal ya está visible.</summary>
internal static class WindowOwnership
{
    public static Window? ResolveOwner(Window? preferred)
    {
        if (IsShown(preferred))
        {
            return preferred;
        }

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            IsShown(desktop.MainWindow))
        {
            return desktop.MainWindow;
        }

        return null;
    }

    public static bool IsShown(Window? window) =>
        window is { IsVisible: true, IsLoaded: true };
}
