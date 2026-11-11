using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace HiveShock.Avalonia.Views.Dialogs;

public enum CloseChoice
{
    /// <summary>Cerró el diálogo sin elegir: no se hace nada.</summary>
    Cancel,
    Background,
    Quit,
}

/// <summary>
/// La primera vez que se pulsa la X explica qué pasa: HiveShock sigue en la bandeja y el directo continúa.
/// Sin este aviso, una app que «desaparece» pero sigue lanzando efectos confunde (o se queda abierta sin querer).
/// La elección se recuerda y se cambia en Preferencias.
/// </summary>
public sealed class CloseChoiceDialog : Window
{
    public CloseChoice Choice { get; private set; } = CloseChoice.Cancel;

    public CloseChoiceDialog(bool trayAvailable)
    {
        Title = "Cerrar HiveShock";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        var where = trayAvailable
            ? (OperatingSystem.IsMacOS() ? "en la barra de menús" : "en la bandeja del sistema, junto al reloj")
            : "minimizado en la barra de tareas";

        var root = new StackPanel { Margin = new Thickness(24), Spacing = 0 };
        root.Children.Add(new TextBlock
        {
            Text = "¿Dejar HiveShock funcionando en segundo plano?",
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });
        root.Children.Add(new TextBlock
        {
            Text = $"Si sigue {where}, el directo, los efectos y los overlays de OBS siguen funcionando aunque no veas la ventana. " +
                   "Para volver, haz clic en su icono; para cerrarlo del todo, usa «Salir de HiveShock» en el menú del icono.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.8,
            Margin = new Thickness(0, 0, 0, 8),
        });
        root.Children.Add(new TextBlock
        {
            Text = "Lo recordaremos. Puedes cambiarlo cuando quieras en Preferencias (arriba a la derecha).",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.65,
            Margin = new Thickness(0, 0, 0, 20),
        });

        var quit = new Button { Content = "Cerrar HiveShock", Classes = { "secondary" } };
        quit.Click += (_, _) =>
        {
            Choice = CloseChoice.Quit;
            Close(true);
        };
        var keep = new Button { Content = "Seguir en segundo plano", Classes = { "primary" }, IsDefault = true };
        keep.Click += (_, _) =>
        {
            Choice = CloseChoice.Background;
            Close(true);
        };
        var cancel = new Button { Content = "Cancelar", Classes = { "ghost" }, IsCancel = true };
        cancel.Click += (_, _) => Close(false);

        var buttons = new DockPanel();
        DockPanel.SetDock(cancel, Dock.Left);
        buttons.Children.Add(cancel);
        var right = new StackPanel
        {
            Classes = { "bar" },
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { quit, keep },
        };
        buttons.Children.Add(right);
        root.Children.Add(buttons);
        Content = root;

        // El teclado va al diálogo (Intro = seguir en segundo plano, Esc = cancelar), no a la ventana de detrás.
        Opened += (_, _) =>
        {
            Activate();
            keep.Focus();
        };
    }
}
