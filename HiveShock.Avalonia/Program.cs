using Avalonia;
using System;

namespace HiveShock.Avalonia;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        // Ayudante de actualización: aplica la versión descargada sin abrir la interfaz.
        if (args.Length == 2 && args[0] == "--apply-update")
        {
            return HiveShock.Updates.UpdateApplier.RunFromPlanFile(args[1]);
        }

        // Una sola copia por usuario: si ya hay una (quizá escondida en la bandeja), se le pide que se muestre.
        if (!HiveShock.Avalonia.Services.SingleInstance.TryAcquire())
        {
            if (HiveShock.Avalonia.Services.SingleInstance.SignalExisting())
            {
                return 0;
            }

            // La otra copia no responde (colgada o cerrándose): mejor arrancar que dejar al usuario sin app.
        }

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            HiveShock.Avalonia.Services.SingleInstance.Release();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
