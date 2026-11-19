namespace HiveShock;

/// <summary>
/// Plataformas móviles (Android e iOS): carpeta de datos propia de la app, sin procesos externos
/// y con el host del juego en la IP del PC, que el usuario escribe a mano.
/// </summary>
public static class MobilePlatform
{
    public static bool IsMobile => OperatingSystem.IsAndroid() || OperatingSystem.IsIOS();
}
