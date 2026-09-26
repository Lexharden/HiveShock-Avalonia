using System.Text.Json;
using System.Text.Json.Serialization;
using HiveShock.Logging;

namespace HiveShock.Configuration;

/// <summary>
/// Guardado redundante de los datos del usuario (preferencias, contador de muertes, voz).
/// Antes vivían solo junto al .exe: una actualización que reemplaza la carpeta (o el
/// HiveShock.app entero en macOS) los borraba. Ahora cada guardado escribe:
/// 1) en la carpeta de datos del usuario (<see cref="UserDirectory"/>), que ninguna
///    actualización toca — es la copia principal;
/// 2) una copia espejo junto al .exe (<see cref="AppPaths.AppDirectory"/>), por si se
///    borra la anterior o se lleva la carpeta portable a otro equipo.
/// Cada escritura es atómica (archivo temporal + reemplazo) y deja el anterior como .bak,
/// así un cierre a mitad de guardado nunca deja un JSON roto. Al cargar se leen todas las
/// copias y gana la válida más reciente.
/// </summary>
public static class UserDataStore
{
    private static readonly object Gate = new();
    private static readonly HashSet<string> WarnedPaths = new(StringComparer.OrdinalIgnoreCase);

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // Las posiciones de overlays empiezan en NaN ("sin colocar"): sin esto la
        // serialización lanza y nada se guarda.
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    /// <summary>
    /// %LOCALAPPDATA%\HiveShock (Windows), ~/Library/Application Support/HiveShock (macOS),
    /// ~/.local/share/HiveShock (Linux). En Android, la carpeta de datos de la app.
    /// </summary>
    public static string UserDirectory
    {
        get
        {
            if (OperatingSystem.IsAndroid())
            {
                return AppPaths.AppDirectory;
            }

            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(root))
            {
                root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            }

            return string.IsNullOrWhiteSpace(root) ? AppPaths.AppDirectory : Path.Combine(root, "HiveShock");
        }
    }

    /// <summary>
    /// Carga la copia válida más reciente entre la carpeta del usuario y la del .exe
    /// (incluidos .bak y nombres antiguos). Null si no hay ninguna.
    /// </summary>
    public static T? Load<T>(string fileName, params string[] legacyFileNames) where T : class
    {
        T? best = null;
        var bestTime = DateTime.MinValue;
        string? bestPath = null;

        foreach (var path in Candidates(fileName, legacyFileNames))
        {
            try
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                var value = JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions);
                var time = File.GetLastWriteTimeUtc(path);
                if (value != null && time > bestTime)
                {
                    best = value;
                    bestTime = time;
                    bestPath = path;
                }
            }
            catch (Exception ex)
            {
                BridgeLog.Warn($"Datos: se ignora una copia dañada ({Path.GetFileName(path)}): {ex.Message}");
            }
        }

        if (bestPath != null)
        {
            BridgeLog.Info($"Datos: {fileName} cargado desde {bestPath}");
        }

        return best;
    }

    /// <summary>Guarda en la carpeta del usuario y en la del .exe. Nunca lanza.</summary>
    public static void Save<T>(string fileName, T value)
    {
        string json;
        try
        {
            json = JsonSerializer.Serialize(value, JsonOptions) + Environment.NewLine;
        }
        catch (Exception ex)
        {
            BridgeLog.Error($"Datos: no se pudo preparar {fileName}: {ex.Message}");
            return;
        }

        lock (Gate)
        {
            var saved = 0;
            foreach (var dir in Directories())
            {
                if (TryWriteAtomic(Path.Combine(dir, fileName), json))
                {
                    saved++;
                }
            }

            if (saved == 0)
            {
                BridgeLog.Error($"Datos: no se pudo guardar {fileName} en ninguna ubicación.");
            }
        }
    }

    private static IEnumerable<string> Directories()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in new[] { UserDirectory, AppPaths.AppDirectory })
        {
            if (!string.IsNullOrWhiteSpace(dir) && seen.Add(Path.GetFullPath(dir)))
            {
                yield return dir;
            }
        }
    }

    private static IEnumerable<string> Candidates(string fileName, string[] legacyFileNames)
    {
        foreach (var dir in Directories())
        {
            foreach (var name in legacyFileNames.Prepend(fileName))
            {
                var path = Path.Combine(dir, name);
                yield return path;
                yield return path + ".bak";
            }
        }
    }

    private static bool TryWriteAtomic(string path, string json)
    {
        try
        {
            AtomicFile.WriteAllText(path, json);
            return true;
        }
        catch (Exception ex)
        {
            // Típico: carpeta del .exe sin permisos de escritura (Archivos de programa). La otra copia basta.
            lock (WarnedPaths)
            {
                if (WarnedPaths.Add(path))
                {
                    BridgeLog.Warn($"Datos: no se pudo escribir {path}: {ex.Message}");
                }
            }

            return false;
        }
    }
}
