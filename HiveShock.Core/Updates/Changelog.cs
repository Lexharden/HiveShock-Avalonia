using System.Globalization;

namespace HiveShock.Updates;

/// <summary>Una versión del CHANGELOG: «## [3.0.0-beta.2] - 2026-11-11» y su texto (Markdown) hasta la siguiente.</summary>
public sealed record ChangelogEntry(string Version, string? Date, string Body)
{
    public SemVer? Semver { get; } = SemVer.TryParse(Version);

    /// <summary>«11 nov 2026», o vacío si no hay fecha válida.</summary>
    public string DateText =>
        DateTime.TryParseExact(Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("d MMM yyyy", CultureInfo.GetCultureInfo("es-ES"))
            : "";
}

/// <summary>
/// Lee CHANGELOG.md (formato Keep a Changelog) para enseñar en la app qué hay de nuevo.
/// Es la misma fuente que las notas del release en GitHub: lo que se lee allí y aquí es idéntico.
/// </summary>
public static class Changelog
{
    /// <summary>Las versiones publicadas, en el orden del archivo. «[Unreleased]» y cabeceras sin versión se ignoran.</summary>
    public static IReadOnlyList<ChangelogEntry> Parse(string markdown)
    {
        var entries = new List<ChangelogEntry>();
        string? version = null;
        string? date = null;
        var body = new List<string>();

        void Flush()
        {
            if (version is not null)
            {
                entries.Add(new ChangelogEntry(version, date, string.Join('\n', body).Trim()));
            }

            version = null;
            date = null;
            body.Clear();
        }

        foreach (var raw in (markdown ?? "").Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.StartsWith("## [", StringComparison.Ordinal))
            {
                Flush();
                var close = line.IndexOf(']', 4);
                var name = close > 4 ? line[4..close].Trim() : "";
                if (SemVer.TryParse(name) is null)
                {
                    continue; // [Unreleased] u otra cabecera: no es una versión publicada
                }

                version = name;
                var dash = line.IndexOf(" - ", close, StringComparison.Ordinal);
                date = dash > 0 ? line[(dash + 3)..].Trim() : null;
                continue;
            }

            if (version is not null)
            {
                body.Add(line);
            }
        }

        Flush();
        return entries;
    }

    /// <summary>
    /// Qué enseñar al abrir <paramref name="current"/> viniendo de <paramref name="lastSeen"/>: las versiones posteriores
    /// a la última vista y hasta la actual (de la más nueva a la más antigua).
    /// Sin versión anterior conocida (quien actualiza desde una versión sin esta ventana), lo nuevo desde la última
    /// versión estable anterior: p. ej. al abrir 3.0.0-beta.2, la beta.2 y la beta.1, pero no la 2.12.5.
    /// </summary>
    public static IReadOnlyList<ChangelogEntry> Between(IEnumerable<ChangelogEntry> entries, string? lastSeen, string current)
    {
        var now = SemVer.TryParse(current);
        if (now is null)
        {
            return [];
        }

        var all = entries.Where(e => e.Semver is not null).ToList();
        var floor = SemVer.TryParse(lastSeen)
                    ?? all.Select(e => e.Semver!)
                        .Where(v => !v.IsPrerelease && v < now)
                        .OrderByDescending(v => v)
                        .FirstOrDefault();

        return all
            .Where(e => e.Semver! <= now && (floor is null || e.Semver! > floor))
            .OrderByDescending(e => e.Semver)
            .ToList();
    }
}
