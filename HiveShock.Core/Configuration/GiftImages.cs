using System.Text.RegularExpressions;

namespace HiveShock.Configuration;

/// <summary>
/// Busca imágenes de regalos en la carpeta <c>gifts-images/</c> junto al exe.
/// Convención: <c>{id}.webp</c> o <c>{nombre-slug}.webp</c> (también png/jpg).
/// </summary>
public static class GiftImages
{
    private static readonly string[] Extensions = [".webp", ".png", ".jpg", ".jpeg"];

    public static string DirectoryPath =>
        Path.Combine(AppPaths.AppDirectory, "gifts-images");

    public static void EnsureDirectory()
    {
        Directory.CreateDirectory(DirectoryPath);
    }

    /// <summary>Ruta absoluta si existe; null si no hay imagen.</summary>
    public static string? ResolvePath(string? id, string? name, string? explicitPath = null)
    {
        EnsureDirectory();

        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var trimmed = explicitPath.Trim();
            var full = Path.IsPathRooted(trimmed)
                ? trimmed
                : Path.Combine(AppPaths.AppDirectory, trimmed.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(full))
            {
                return full;
            }
        }

        foreach (var candidate in CandidatePaths(id, name))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static IReadOnlyList<string> CandidatePaths(string? id, string? name)
    {
        EnsureDirectory();
        var list = new List<string>();
        var dir = DirectoryPath;

        if (!string.IsNullOrWhiteSpace(id))
        {
            var key = id.Trim();
            foreach (var ext in Extensions)
            {
                list.Add(Path.Combine(dir, key + ext));
            }
        }

        var slug = Slugify(name);
        if (!string.IsNullOrWhiteSpace(slug))
        {
            foreach (var ext in Extensions)
            {
                list.Add(Path.Combine(dir, slug + ext));
            }
        }

        foreach (var alias in SplitAliases(name))
        {
            var aliasSlug = Slugify(alias);
            if (string.IsNullOrWhiteSpace(aliasSlug) || aliasSlug == slug)
            {
                continue;
            }

            foreach (var ext in Extensions)
            {
                list.Add(Path.Combine(dir, aliasSlug + ext));
            }
        }

        return list;
    }

    /// <summary>Archivos de imagen en gifts-images/ (solo nombre, sin ruta).</summary>
    public static IReadOnlyList<string> ListFiles()
    {
        EnsureDirectory();
        return Directory
            .EnumerateFiles(DirectoryPath)
            .Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Cast<string>()
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string Slugify(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "";
        }

        var lower = name.Trim().ToLowerInvariant();
        lower = Regex.Replace(lower, @"[^a-z0-9]+", "-");
        lower = lower.Trim('-');
        return lower;
    }

    private static IEnumerable<string> SplitAliases(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            yield break;
        }

        foreach (var part in name.Split('/', '|', ',', ';'))
        {
            var t = part.Trim();
            if (t.Length > 0)
            {
                yield return t;
            }
        }
    }
}
