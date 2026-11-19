using HiveShock.Logging;
using HiveShock.Updates;

namespace HiveShock.Avalonia.Services;

/// <summary>
/// «Novedades»: las notas del CHANGELOG que va dentro del programa.
/// Al abrir una versión nueva se enseñan una vez las de las versiones que el usuario aún no ha visto;
/// en una instalación nueva no (para eso está el tutorial).
/// </summary>
public static class WhatsNew
{
    private const string ResourceName = "HiveShock.CHANGELOG.md";

    private static readonly Lazy<IReadOnlyList<ChangelogEntry>> AllEntries = new(Load);

    /// <summary>Todas las versiones publicadas del CHANGELOG incluido en el programa.</summary>
    public static IReadOnlyList<ChangelogEntry> All => AllEntries.Value;

    /// <summary>
    /// Lo que hay que enseñar al arrancar, y lo marca como visto (aunque luego se cierre sin leer: solo sale una vez).
    /// Vacío si es una instalación nueva, si ya se vio esta versión o si no hay notas.
    /// </summary>
    public static IReadOnlyList<ChangelogEntry> TakePendingOnStartup(UiPreferences prefs, string currentVersion)
    {
        var lastSeen = prefs.LastSeenVersion;
        if (string.Equals(lastSeen, currentVersion, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        prefs.LastSeenVersion = currentVersion;
        prefs.Save();
        return prefs.IsFirstRun ? [] : Changelog.Between(All, lastSeen.Length > 0 ? lastSeen : null, currentVersion);
    }

    /// <summary>Las novedades de la versión instalada (y las preliminares de la misma), para el botón «Novedades».</summary>
    public static IReadOnlyList<ChangelogEntry> ForVersion(string currentVersion)
    {
        var entries = Changelog.Between(All, null, currentVersion);
        // Una versión de desarrollo sin sección propia: al menos la última publicada.
        return entries.Count > 0 ? entries : All.Take(1).ToList();
    }

    private static IReadOnlyList<ChangelogEntry> Load()
    {
        try
        {
            using var stream = typeof(WhatsNew).Assembly.GetManifestResourceStream(ResourceName);
            if (stream is null)
            {
                return [];
            }

            using var reader = new StreamReader(stream);
            return Changelog.Parse(reader.ReadToEnd());
        }
        catch (Exception ex)
        {
            BridgeLog.Info($"Novedades: no se pudieron leer ({ex.Message}).");
            return [];
        }
    }
}
