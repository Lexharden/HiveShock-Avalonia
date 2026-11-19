namespace HiveShock.Configuration;

/// <summary>
/// Assets que son archivos normales de una carpeta de solo lectura (en iOS, el bundle de la app).
/// Rechaza rutas que salgan de la carpeta raíz.
/// </summary>
public sealed class DirectoryAssetSource : IAssetSource
{
    private readonly string _root;

    public DirectoryAssetSource(string root) =>
        _root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

    public IReadOnlyList<string> List(string path)
    {
        var full = Resolve(path);
        if (full is null || !Directory.Exists(full))
        {
            return [];
        }

        return Directory.EnumerateFileSystemEntries(full)
            .Select(Path.GetFileName)
            .OfType<string>()
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }

    public Stream? Open(string path)
    {
        var full = Resolve(path);
        return full is not null && File.Exists(full) ? File.OpenRead(full) : null;
    }

    private string? Resolve(string path)
    {
        var full = Path.GetFullPath(Path.Combine(_root, path.Replace('/', Path.DirectorySeparatorChar)));
        return full.StartsWith(_root, StringComparison.OrdinalIgnoreCase) || full + Path.DirectorySeparatorChar == _root
            ? full
            : null;
    }
}
