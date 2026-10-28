using System.Globalization;
using HiveShock.Input;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace HiveShock.Configuration;

public static class AppPaths
{
    /// <summary>Directory that holds the published exe (not the single-file extract folder). On Android: app data.</summary>
    public static string AppDirectory
    {
        get
        {
            if (OperatingSystem.IsAndroid())
            {
                var data = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrWhiteSpace(data))
                {
                    data = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                }

                if (!string.IsNullOrWhiteSpace(data))
                {
                    Directory.CreateDirectory(data);
                    return data.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }
            }

            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath))
            {
                var dir = Path.GetDirectoryName(processPath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    return dir;
                }
            }

            return AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    public static string? Find(string fileName)
    {
        foreach (var root in CandidateRoots())
        {
            var path = Path.Combine(root, fileName);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    public static string Require(string fileName) =>
        Find(fileName) ?? throw new FileNotFoundException(
            $"No se encontró {fileName} (carpeta: {AppDirectory}).");

    public static IEnumerable<string> CandidateRootsPublic() => CandidateRoots();

    private static IEnumerable<string> CandidateRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in EnumerateRootCandidates())
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            string full;
            try
            {
                full = Path.GetFullPath(raw);
            }
            catch
            {
                continue;
            }

            if (!seen.Add(full))
            {
                continue;
            }

            yield return full;
        }
    }

    private static IEnumerable<string> EnumerateRootCandidates()
    {
        yield return AppDirectory;
        yield return AppContext.BaseDirectory;
        yield return Directory.GetCurrentDirectory();

        foreach (var macRoot in EnumerateMacBundleRoots(AppDirectory))
        {
            yield return macRoot;
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir != null; i++)
        {
            yield return dir.FullName;
            dir = dir.Parent;
        }
    }

    /// <summary>
    /// HiveShock.app/Contents/MacOS → Resources y Contents, para datos junto al bundle.
    /// </summary>
    private static IEnumerable<string> EnumerateMacBundleRoots(string appDirectory)
    {
        DirectoryInfo? macOs;
        try
        {
            macOs = new DirectoryInfo(appDirectory);
        }
        catch
        {
            yield break;
        }

        if (!string.Equals(macOs.Name, "MacOS", StringComparison.Ordinal) ||
            macOs.Parent is not { Name: "Contents" } contents)
        {
            yield break;
        }

        yield return Path.Combine(contents.FullName, "Resources");
        yield return contents.FullName;
        if (contents.Parent != null)
        {
            yield return contents.Parent.FullName;
        }
    }
}
