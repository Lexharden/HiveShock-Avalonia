using System.Text.Json;
using System.Text.Json.Serialization;
using HiveShock.Configuration;
using HiveShock.Logging;

namespace HiveShock.Input;

/// <summary>
/// Biblioteca de macros de teclas del usuario: independiente de cualquier juego del catálogo.
/// Incluye la ventana a la que se envían las teclas. Los perfiles solo referencian macros por id.
/// </summary>
public sealed class MacroLibrary
{
    public static readonly MacroLibrary Empty = new();

    public WindowTarget? Window { get; init; }

    public List<KeyMacro> Macros { get; init; } = [];
}

/// <summary>
/// Guardado de la <see cref="MacroLibrary"/> (<c>macros.json</c>). Igual que <see cref="UserDataStore"/>:
/// copia principal en la carpeta de datos del usuario (que las actualizaciones no tocan) y espejo junto al
/// .exe; cada escritura es atómica con <c>.bak</c>, y al cargar gana la copia válida más reciente.
/// </summary>
public static class MacroLibraryStore
{
    public const string FileName = "macros.json";

    private static readonly object Gate = new();

    private static readonly JsonSerializerOptions Options = new(JsonDefaults.Options)
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static IReadOnlyList<string> DefaultDirectories() =>
        new[] { UserDataStore.UserDirectory, AppPaths.AppDirectory }
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Carga la biblioteca. Si todavía no existe (primera vez tras actualizar) importa las macros y la ventana
    /// del <c>keys.json</c> / <c>profile.json</c> del perfil activo y las guarda ya en la biblioteca.
    /// </summary>
    public static MacroLibrary LoadOrImport(LoadedGameProfile legacyProfile) =>
        LoadOrImport(DefaultDirectories(), () => ImportLegacy(legacyProfile));

    public static MacroLibrary LoadOrImport(IReadOnlyList<string> directories, Func<MacroLibrary?> legacy)
    {
        var found = Load(directories);
        if (found != null)
        {
            return found;
        }

        var imported = legacy();
        if (imported == null || (imported.Macros.Count == 0 && imported.Window == null))
        {
            return MacroLibrary.Empty;
        }

        try
        {
            Save(directories, imported);
            BridgeLog.Info($"Macros: {imported.Macros.Count} importadas a la biblioteca global ({FileName}).");
        }
        catch (Exception ex)
        {
            BridgeLog.Warn($"Macros: no se pudo crear {FileName}: {ex.Message}");
        }

        return imported;
    }

    /// <summary>Copia válida más reciente entre todas las ubicaciones (con sus .bak). Null si no hay ninguna.</summary>
    public static MacroLibrary? Load(IReadOnlyList<string> directories)
    {
        MacroLibrary? best = null;
        var bestTime = DateTime.MinValue;
        foreach (var dir in directories)
        {
            var path = Path.Combine(dir, FileName);
            var text = AtomicFile.ReadAllTextWithBackup(path, IsValid, out var usedBackup);
            if (text == null)
            {
                continue;
            }

            if (usedBackup)
            {
                BridgeLog.Warn($"{path} estaba dañado: se recuperó la copia anterior (.bak).");
            }

            var time = LastWrite(usedBackup ? path + ".bak" : path);
            if (time >= bestTime && Parse(text) is { } library)
            {
                best = library;
                bestTime = time;
            }
        }

        return best;
    }

    /// <summary>Guarda en todas las ubicaciones. Lanza solo si no se pudo escribir en ninguna.</summary>
    public static void Save(IReadOnlyList<string> directories, MacroLibrary library)
    {
        var json = JsonSerializer.Serialize(
            new FileShape
            {
                Window = library.Window is { IsEmpty: false } ? library.Window : null,
                Macros = [.. library.Macros],
            },
            Options) + Environment.NewLine;

        lock (Gate)
        {
            Exception? last = null;
            var saved = 0;
            foreach (var dir in directories)
            {
                try
                {
                    AtomicFile.WriteAllText(Path.Combine(dir, FileName), json);
                    saved++;
                }
                catch (Exception ex)
                {
                    last = ex;
                }
            }

            if (saved == 0)
            {
                throw new IOException($"No se pudo guardar {FileName} en ninguna ubicación: {last?.Message}", last);
            }
        }
    }

    public static void Save(MacroLibrary library) => Save(DefaultDirectories(), library);

    private static MacroLibrary? ImportLegacy(LoadedGameProfile profile)
    {
        var macros = KeyMacroCatalogStore.Load(profile.KeysPath).All.ToList();
        var window = profile.Info.Window is { IsEmpty: false } w ? w : null;
        return macros.Count == 0 && window == null ? null : new MacroLibrary { Window = window, Macros = macros };
    }

    private sealed class FileShape
    {
        public WindowTarget? Window { get; set; }

        public List<KeyMacro> Macros { get; set; } = [];
    }

    private static MacroLibrary? Parse(string text)
    {
        try
        {
            var shape = JsonSerializer.Deserialize<FileShape>(text, Options);
            return shape == null ? null : new MacroLibrary { Window = shape.Window, Macros = shape.Macros ?? [] };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsValid(string text) => Parse(text) != null;

    private static DateTime LastWrite(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch (IOException)
        {
            return DateTime.MinValue;
        }
    }
}
