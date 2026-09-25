using HiveShock.Configuration;

namespace HiveShock.Voice.Piper;

/// <summary>
/// Dónde vive todo lo de Piper: en la carpeta de datos del usuario (%LOCALAPPDATA%\HiveShock\piper
/// en Windows), no junto al .exe. Pesa decenas o cientos de MB y debe sobrevivir a las
/// actualizaciones de HiveShock, igual que el resto de datos (<see cref="UserDataStore"/>).
/// </summary>
public sealed class PiperPaths
{
    public PiperPaths(string root)
    {
        Root = root;
    }

    public static PiperPaths Default => new(Path.Combine(UserDataStore.UserDirectory, "piper"));

    public string Root { get; }

    /// <summary>Motor descomprimido: runtime/piper/piper(.exe), espeak-ng-data, onnxruntime…</summary>
    public string RuntimeDirectory => Path.Combine(Root, "runtime");

    /// <summary>Una carpeta por voz instalada (voices/es_MX-claude-high/…).</summary>
    public string VoicesDirectory => Path.Combine(Root, "voices");

    /// <summary>Descargas a medias y WAV intermedios; se puede borrar sin perder nada.</summary>
    public string TempDirectory => Path.Combine(Root, "tmp");

    /// <summary>Última copia del catálogo oficial, para funcionar sin internet.</summary>
    public string CatalogCachePath => Path.Combine(Root, "voices.json");
}
