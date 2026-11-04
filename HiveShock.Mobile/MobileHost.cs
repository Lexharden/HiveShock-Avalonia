using HiveShock.Hosting;
using HiveShock.Logging;

namespace HiveShock.Mobile;

/// <summary>Lo que la UI móvil necesita de cada plataforma (Android, iOS). Cada proyecto de arranque aporta la suya.</summary>
public interface IMobileHost
{
    /// <summary>El puente de este proceso: uno solo, para que recrear la pantalla no corte TikTok ni Twitch.</summary>
    BridgeRuntime Runtime { get; }

    /// <summary>Abre la URL en el navegador del sistema.</summary>
    void OpenUrl(string url);

    /// <summary>Copia el texto al portapapeles.</summary>
    void CopyText(string text);

    /// <summary>Mantiene el puente vivo con la app en segundo plano (servicio en Android; en iOS, pantalla encendida).</summary>
    void StartKeepAlive();

    void StopKeepAlive();
}

/// <summary>Punto de acceso a la <see cref="IMobileHost"/> registrada. El proyecto de arranque la asigna antes de crear la UI.</summary>
public static class MobileHost
{
    private static IMobileHost? _current;

    public static IMobileHost Current
    {
        get => _current ?? throw new InvalidOperationException("La plataforma móvil no se registró (MobileHost.Current).");
        set
        {
            _current = value;
            PlatformShell.UrlLauncher = value.OpenUrl;
        }
    }

    public static BridgeRuntime Runtime => Current.Runtime;

    public static void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            Current.OpenUrl(url);
        }
        catch (Exception ex)
        {
            BridgeLog.Warn($"Navegador: {ex.Message}");
        }
    }

    public static void CopyText(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            Current.CopyText(text);
        }
    }

    public static void StartKeepAlive() => Current.StartKeepAlive();

    public static void StopKeepAlive() => Current.StopKeepAlive();
}
