using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace HiveShock.Updates;

/// <summary>Carpetas y archivos de trabajo de las actualizaciones (por defecto bajo la carpeta del usuario).</summary>
public sealed class UpdatePaths
{
    public UpdatePaths(string root) => Root = root;

    public string Root { get; }
    public string Downloads(string version) => Path.Combine(Root, "downloads", version);
    public string Staging => Path.Combine(Root, "staging");
    public string Backup => Path.Combine(Root, "backup");
    public string Helper => Path.Combine(Root, "helper");
    public string PlanFile => Path.Combine(Root, "apply-plan.json");
    public string ResultFile => Path.Combine(Root, "update-result.json");
    public string OkMarker => Path.Combine(Root, "update-ok");
    public string PendingFile => Path.Combine(Root, "pending.json");
    public string CacheFile => Path.Combine(Root, "releases-cache.json");

    public static UpdatePaths Default() =>
        new(Path.Combine(HiveShock.Configuration.UserDataStore.UserDirectory, "updates"));
}

/// <summary>Dónde van los archivos del zip y qué datos del usuario no se tocan.</summary>
public sealed record UpdateLayout(string TargetRoot, string SourceRoot, string DataPrefix, string ExecutableRelative)
{
    private static readonly string[] ProtectedPrefixes = ["profiles/", "logs/", "updates/"];
    private static readonly string[] ProtectedFiles = [".env", "gift-catalog.json", "active-profile.txt"];

    /// <summary>
    /// Windows/Linux: el zip trae los archivos en la raíz y se fusionan en la carpeta de la app.
    /// macOS: el zip trae HiveShock.app y se fusiona sobre el .app instalado (los datos del usuario
    /// viven en Contents/MacOS, así que se fusiona archivo a archivo en vez de reemplazar el bundle).
    /// </summary>
    public static UpdateLayout Detect(string appDir, string stagingDir)
    {
        appDir = appDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var apps = Directory.GetDirectories(stagingDir, "*.app");
        if (apps.Length > 0)
        {
            var macos = new DirectoryInfo(appDir);
            var contents = macos.Parent;
            var bundle = contents?.Parent;
            if (!string.Equals(macos.Name, "MacOS", StringComparison.Ordinal) ||
                !string.Equals(contents?.Name, "Contents", StringComparison.Ordinal) ||
                bundle is null)
            {
                throw new UpdateException("HiveShock no está dentro de un HiveShock.app; instala la actualización a mano.");
            }

            return new UpdateLayout(bundle.FullName, apps[0], "Contents/MacOS/", "Contents/MacOS/HiveShock");
        }

        var source = stagingDir;
        var files = Directory.GetFiles(source);
        var dirs = Directory.GetDirectories(source);
        if (files.Length == 0 && dirs.Length == 1)
        {
            source = dirs[0];
        }

        var exe = OperatingSystem.IsWindows() ? "HiveShock.exe" : "HiveShock";
        return new UpdateLayout(appDir, source, "", exe);
    }

    /// <summary>True si la ruta (relativa al zip, con «/») es un dato del usuario que no se sobrescribe.</summary>
    public bool IsProtected(string relativePath)
    {
        var rel = relativePath.Replace('\\', '/');
        if (DataPrefix.Length > 0)
        {
            if (!rel.StartsWith(DataPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            rel = rel[DataPrefix.Length..];
        }

        if (rel.StartsWith(".hiveshock", StringComparison.OrdinalIgnoreCase) ||
            rel.StartsWith(".bridge", StringComparison.OrdinalIgnoreCase) ||
            rel.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return ProtectedFiles.Contains(rel, StringComparer.OrdinalIgnoreCase) ||
               ProtectedPrefixes.Any(p => rel.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class ApplyPlan
{
    public int Pid { get; set; }
    public string TargetRoot { get; set; } = "";
    public string SourceRoot { get; set; } = "";
    public string DataPrefix { get; set; } = "";
    public string BackupDir { get; set; } = "";
    public string StagingDir { get; set; } = "";
    public string Executable { get; set; } = "";
    public string FromVersion { get; set; } = "";
    public string ToVersion { get; set; } = "";
    public string ResultFile { get; set; } = "";
    public string OkMarker { get; set; } = "";
    public int OkTimeoutSeconds { get; set; } = 25;
}

public sealed class UpdateResult
{
    public bool Ok { get; set; }
    public bool RolledBack { get; set; }
    public string FromVersion { get; set; } = "";
    public string ToVersion { get; set; } = "";
    public string Error { get; set; } = "";
    public DateTimeOffset AtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public static class UpdateInstaller
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Extrae el zip a la carpeta de preparación (rechazando rutas fuera de ella) y comprueba que traiga el ejecutable.</summary>
    public static UpdateLayout Stage(string zipPath, UpdatePaths paths, string appDir)
    {
        var staging = paths.Staging;
        DeleteDirectory(staging);
        Directory.CreateDirectory(staging);

        try
        {
            var root = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
            using var zip = ZipFile.OpenRead(zipPath);
            foreach (var entry in zip.Entries)
            {
                var dest = Path.GetFullPath(Path.Combine(staging, entry.FullName));
                if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    throw new UpdateException("El zip contiene rutas no permitidas; se descartó por seguridad.");
                }

                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                {
                    Directory.CreateDirectory(dest);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                entry.ExtractToFile(dest, overwrite: true);
                if (!OperatingSystem.IsWindows())
                {
                    var mode = (UnixFileMode)((entry.ExternalAttributes >> 16) & 0x1FF);
                    if (mode != UnixFileMode.None)
                    {
                        File.SetUnixFileMode(dest, mode);
                    }
                }
            }

            var layout = UpdateLayout.Detect(appDir, staging);
            var exe = Path.Combine(layout.SourceRoot, layout.ExecutableRelative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(exe))
            {
                throw new UpdateException("El paquete no contiene el ejecutable de HiveShock; se descartó.");
            }

            if (OperatingSystem.IsWindows() && !HasPeHeader(exe))
            {
                throw new UpdateException("El ejecutable del paquete está dañado; se descartó.");
            }

            return layout;
        }
        catch
        {
            DeleteDirectory(staging);
            throw;
        }
    }

    private static bool HasPeHeader(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            return fs.Length > 1024 && fs.ReadByte() == 'M' && fs.ReadByte() == 'Z';
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>Guarda el plan, lanza el ayudante que aplica la actualización y devuelve. El llamador debe cerrar HiveShock enseguida.</summary>
    public static void LaunchApplier(UpdateLayout layout, UpdatePaths paths, string fromVersion, string toVersion)
    {
        var exePath = Environment.ProcessPath ?? throw new UpdateException("No se encontró el ejecutable actual.");
        var plan = new ApplyPlan
        {
            Pid = Environment.ProcessId,
            TargetRoot = layout.TargetRoot,
            SourceRoot = layout.SourceRoot,
            DataPrefix = layout.DataPrefix,
            BackupDir = paths.Backup,
            StagingDir = paths.Staging,
            Executable = Path.Combine(layout.TargetRoot, layout.ExecutableRelative.Replace('/', Path.DirectorySeparatorChar)),
            FromVersion = fromVersion,
            ToVersion = toVersion,
            ResultFile = paths.ResultFile,
            OkMarker = paths.OkMarker,
        };

        Directory.CreateDirectory(paths.Root);
        File.WriteAllText(paths.PlanFile, JsonSerializer.Serialize(plan, Json));
        File.WriteAllText(paths.PendingFile, JsonSerializer.Serialize(new { ToVersion = toVersion }, Json));
        TryDelete(paths.ResultFile);
        TryDelete(paths.OkMarker);

        // En Windows el .exe en ejecución no se puede sobrescribir: el ayudante corre desde una copia.
        var helper = exePath;
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(paths.Helper);
            helper = Path.Combine(paths.Helper, "HiveShock-updater.exe");
            File.Copy(exePath, helper, overwrite: true);
        }

        var psi = new ProcessStartInfo(helper)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(helper)!,
        };
        psi.ArgumentList.Add("--apply-update");
        psi.ArgumentList.Add(paths.PlanFile);
        Process.Start(psi);
    }

    public static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // ignore
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // ignore
        }
    }
}
