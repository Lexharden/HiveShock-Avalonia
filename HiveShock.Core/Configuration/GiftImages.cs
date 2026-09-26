using System.Text.RegularExpressions;

namespace HiveShock.Configuration;

/// <summary>
/// Busca imágenes de regalos en la carpeta <c>gifts-images/</c> junto al exe. Nombres aceptados:
/// <c>{id}_{Nombre}.webp</c> (el formato de tiktok_gifts.json, ej. <c>9427_Pegasus.webp</c>),
/// <c>{id}.webp</c> y <c>{nombre-slug}.webp</c>; también png/jpg. Se indexa la carpeta una
/// vez y se reindexa sola cuando cambia (archivos añadidos, borrados o renombrados), en vez
/// de comprobar decenas de rutas posibles por cada imagen.
/// </summary>
public static partial class GiftImages
{
    private static readonly string[] Extensions = [".webp", ".png", ".jpg", ".jpeg"];
    private static readonly object Gate = new();
    private static Index? _index;

    public static string DirectoryPath =>
        Path.Combine(AppPaths.AppDirectory, "gifts-images");

    public static void EnsureDirectory()
    {
        Directory.CreateDirectory(DirectoryPath);
    }

    /// <summary>Fuerza a volver a leer la carpeta (tras copiar imágenes a mano, por ejemplo).</summary>
    public static void Invalidate()
    {
        lock (Gate)
        {
            _index = null;
        }
    }

    /// <summary>
    /// Ruta absoluta si existe; null si no hay imagen. Orden: ruta explícita (absoluta, relativa
    /// al exe o solo el nombre del archivo dentro de gifts-images) → id → nombre y alias
    /// separados por "/", "|", "," o ";".
    /// </summary>
    public static string? ResolvePath(string? id, string? name, string? explicitPath = null) =>
        ResolvePathForNames(id, [name], explicitPath);

    public static string? ResolvePathForNames(string? id, IEnumerable<string?> names, string? explicitPath = null)
    {
        var index = GetIndex();

        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var trimmed = explicitPath.Trim().Replace('\\', '/');
            var full = Path.IsPathRooted(trimmed)
                ? trimmed
                : Path.Combine(AppPaths.AppDirectory, trimmed.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(full))
            {
                return full;
            }

            // "gift_images\9427_Pegasus.webp" con la carpeta llamada distinto: vale el nombre del archivo.
            if (index.ByFileName.TryGetValue(Path.GetFileName(trimmed), out var byFile))
            {
                return byFile;
            }
        }

        if (!string.IsNullOrWhiteSpace(id) && index.ById.TryGetValue(id.Trim(), out var byId))
        {
            return byId;
        }

        foreach (var name in names)
        {
            foreach (var alias in SplitAliases(name))
            {
                var slug = Slugify(alias);
                if (slug.Length > 0 && index.BySlug.TryGetValue(slug, out var bySlug))
                {
                    return bySlug;
                }
            }
        }

        return null;
    }

    /// <summary>Archivos de imagen en gifts-images/ (solo nombre, sin ruta).</summary>
    public static IReadOnlyList<string> ListFiles() =>
        GetIndex().Files.Select(Path.GetFileName).Cast<string>().ToList();

    public static string Slugify(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "";
        }

        var lower = name.Trim().ToLowerInvariant();
        lower = NonAlphanumericRegex().Replace(lower, "-");
        return lower.Trim('-');
    }

    private static Index GetIndex()
    {
        var dir = DirectoryPath;
        var stamp = Directory.Exists(dir) ? Directory.GetLastWriteTimeUtc(dir) : DateTime.MinValue;
        lock (Gate)
        {
            if (_index != null && _index.Directory == dir && _index.Stamp == stamp)
            {
                return _index;
            }

            _index = Index.Build(dir, stamp);
            return _index;
        }
    }

    private static IEnumerable<string> SplitAliases(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            yield break;
        }

        yield return name.Trim();
        foreach (var part in name.Split('/', '|', ',', ';'))
        {
            var t = part.Trim();
            if (t.Length > 0 && t != name.Trim())
            {
                yield return t;
            }
        }
    }

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonAlphanumericRegex();

    [GeneratedRegex(@"^(\d+)(?:[_\-\s]+(.*))?$")]
    private static partial Regex IdPrefixRegex();

    /// <summary>Foto de la carpeta: por id, por nombre (slug) y por nombre de archivo.</summary>
    private sealed class Index
    {
        public required string Directory { get; init; }
        public required DateTime Stamp { get; init; }
        public List<string> Files { get; } = [];
        public Dictionary<string, string> ById { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> BySlug { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> ByFileName { get; } = new(StringComparer.OrdinalIgnoreCase);

        public static Index Build(string dir, DateTime stamp)
        {
            var index = new Index { Directory = dir, Stamp = stamp };
            if (!System.IO.Directory.Exists(dir))
            {
                return index;
            }

            // Orden estable: webp antes que png/jpg y, a igualdad, id menor primero (nombres repetidos
            // en tiktok_gifts.json con otro id: se queda la versión más antigua del regalo).
            var files = System.IO.Directory.EnumerateFiles(dir)
                .Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .OrderBy(f => Array.IndexOf(Extensions, Path.GetExtension(f).ToLowerInvariant()))
                .ThenBy(f => IdPrefixRegex().Match(Path.GetFileNameWithoutExtension(f)) is { Success: true } m
                    ? long.TryParse(m.Groups[1].Value, out var n) ? n : long.MaxValue
                    : long.MaxValue)
                .ThenBy(f => f, StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                index.Files.Add(file);
                index.ByFileName.TryAdd(Path.GetFileName(file), file);

                var stem = Path.GetFileNameWithoutExtension(file);
                var match = IdPrefixRegex().Match(stem);
                if (match.Success)
                {
                    index.ById.TryAdd(match.Groups[1].Value, file);
                    var namePart = Slugify(match.Groups[2].Value.Replace('_', ' '));
                    if (namePart.Length > 0)
                    {
                        index.BySlug.TryAdd(namePart, file);
                    }
                }
                else
                {
                    var slug = Slugify(stem.Replace('_', ' '));
                    if (slug.Length > 0)
                    {
                        index.BySlug.TryAdd(slug, file);
                    }
                }
            }

            index.Files.Sort(StringComparer.OrdinalIgnoreCase);
            return index;
        }
    }
}
