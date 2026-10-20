using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace HiveShock.Updates;

/// <summary>Lo que el ayudante necesita del sistema; se sustituye en las pruebas.</summary>
public interface IApplierHost
{
    /// <summary>Espera a que el proceso termine. False si sigue vivo tras el tiempo dado.</summary>
    bool WaitForExit(int pid, TimeSpan timeout);

    /// <summary>Lanza el ejecutable y devuelve su PID.</summary>
    int Launch(string executable);

    /// <summary>Cierra a la fuerza un proceso que lanzamos nosotros (la versión nueva que no arrancó bien).</summary>
    void Kill(int pid);

    /// <summary>Espera a que exista el archivo (la app nueva confirma que arrancó).</summary>
    bool WaitForFile(string path, TimeSpan timeout);

    void Sleep(TimeSpan time);
}

public sealed class ProcessApplierHost : IApplierHost
{
    public bool WaitForExit(int pid, TimeSpan timeout)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.WaitForExit(timeout);
        }
        catch (ArgumentException)
        {
            return true; // ya no existe
        }
    }

    public int Launch(string executable)
    {
        var psi = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
        };
        return Process.Start(psi)?.Id ?? 0;
    }

    public void Kill(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            p.Kill(entireProcessTree: true);
            p.WaitForExit(5000);
        }
        catch
        {
            // ya terminó
        }
    }

    public bool WaitForFile(string path, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            if (File.Exists(path))
            {
                return true;
            }

            Thread.Sleep(250);
        }

        return File.Exists(path);
    }

    public void Sleep(TimeSpan time) => Thread.Sleep(time);
}

/// <summary>
/// Aplica una actualización ya preparada: respalda lo que va a sobrescribir, fusiona los archivos nuevos
/// sin tocar datos del usuario, relanza HiveShock y, si no confirma que arrancó (o algo falla), restaura el respaldo.
/// </summary>
public static class UpdateApplier
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    [DllImport("kernel32.dll")]
    private static extern uint SetErrorMode(uint mode);

    /// <summary>Punto de entrada del ayudante (<c>--apply-update plan.json</c>). Devuelve el código de salida.</summary>
    public static int RunFromPlanFile(string planFile)
    {
        if (OperatingSystem.IsWindows())
        {
            // Sin esto, lanzar un ejecutable inválido abre un diálogo del sistema que deja al ayudante esperando para siempre.
            SetErrorMode(0x0001 | 0x0002 | 0x8000);
        }

        // Carga por adelantado lo que se usará después, por si el reemplazo cambia archivos de la carpeta de la app.
        _ = Process.GetCurrentProcess().Id;
        ApplyPlan? plan;
        try
        {
            plan = JsonSerializer.Deserialize<ApplyPlan>(File.ReadAllText(planFile));
        }
        catch
        {
            return 2;
        }

        if (plan is null)
        {
            return 2;
        }

        var result = Apply(plan, new ProcessApplierHost());
        WriteResult(plan, result);
        return result.Ok ? 0 : 1;
    }

    public static UpdateResult Apply(ApplyPlan plan, IApplierHost host)
    {
        var result = new UpdateResult { FromVersion = plan.FromVersion, ToVersion = plan.ToVersion };

        if (!host.WaitForExit(plan.Pid, TimeSpan.FromSeconds(60)))
        {
            result.Error = "HiveShock no se cerró a tiempo; no se cambió nada.";
            return result;
        }

        var touched = new List<(string Dest, string? Backup)>();
        var launched = 0;
        try
        {
            if (Directory.Exists(plan.BackupDir))
            {
                Directory.Delete(plan.BackupDir, recursive: true);
            }

            Directory.CreateDirectory(plan.BackupDir);
            var layout = new UpdateLayout(plan.TargetRoot, plan.SourceRoot, plan.DataPrefix, "");

            var newProfiles = layout.FindNewProfiles();
            foreach (var file in Directory.EnumerateFiles(plan.SourceRoot, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(plan.SourceRoot, file).Replace('\\', '/');
                if (layout.IsProtected(rel) && !UpdateLayout.IsInNewProfile(rel, newProfiles))
                {
                    continue;
                }

                var dest = Path.Combine(plan.TargetRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                string? backup = null;
                if (File.Exists(dest))
                {
                    backup = Path.Combine(plan.BackupDir, rel.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    File.Copy(dest, backup, overwrite: true);
                }

                touched.Add((dest, backup));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                ReplaceFile(file, dest, host);
            }

            if (!OperatingSystem.IsWindows() && File.Exists(plan.Executable))
            {
                File.SetUnixFileMode(plan.Executable, File.GetUnixFileMode(plan.Executable) |
                    UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            }

            TryDelete(plan.OkMarker);
            launched = host.Launch(plan.Executable);
            if (!host.WaitForFile(plan.OkMarker, TimeSpan.FromSeconds(plan.OkTimeoutSeconds)))
            {
                throw new UpdateException("La versión nueva no confirmó que arrancó.");
            }

            result.Ok = true;
            return result;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            result.RolledBack = Rollback(touched, plan, host, launched);
            return result;
        }
    }

    private static bool Rollback(List<(string Dest, string? Backup)> touched, ApplyPlan plan, IApplierHost host, int launched)
    {
        var complete = true;
        if (launched != 0)
        {
            host.Kill(launched);
        }

        foreach (var (dest, backup) in touched)
        {
            try
            {
                if (backup is not null)
                {
                    ReplaceFile(backup, dest, host);
                }
                else if (File.Exists(dest))
                {
                    File.Delete(dest);
                }
            }
            catch
            {
                complete = false;
            }
        }

        try
        {
            host.Launch(plan.Executable);
        }
        catch
        {
            complete = false;
        }

        return complete;
    }

    /// <summary>Copia a un archivo temporal junto al destino y lo renombra encima (reintenta si está bloqueado).</summary>
    private static void ReplaceFile(string source, string dest, IApplierHost host)
    {
        var temp = dest + ".hsnew";
        Exception? last = null;
        for (var i = 0; i < 20; i++)
        {
            try
            {
                File.Copy(source, temp, overwrite: true);
                File.Move(temp, dest, overwrite: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                last = ex;
                host.Sleep(TimeSpan.FromMilliseconds(500));
            }
        }

        TryDelete(temp);
        throw new UpdateException($"No se pudo reemplazar {Path.GetFileName(dest)}: {last?.Message}", last);
    }

    private static void WriteResult(ApplyPlan plan, UpdateResult result)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(plan.ResultFile)!);
            File.WriteAllText(plan.ResultFile, JsonSerializer.Serialize(result, Json));
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

/// <summary>Lo que la app hace al arrancar tras una actualización.</summary>
public static class UpdateStartup
{
    /// <summary>Si hay una actualización esperando confirmación y esta es la versión nueva, avisa al ayudante de que arrancó bien.</summary>
    public static void Confirm(UpdatePaths paths, string currentVersion)
    {
        try
        {
            if (!File.Exists(paths.PendingFile))
            {
                return;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(paths.PendingFile));
            var to = doc.RootElement.TryGetProperty("ToVersion", out var v) ? v.GetString() : null;
            if (SemVer.TryParse(to) is { } target && SemVer.TryParse(currentVersion) is { } cur && target.Equals(cur))
            {
                File.WriteAllText(paths.OkMarker, DateTimeOffset.UtcNow.ToString("O"));
                File.Delete(paths.PendingFile);
            }
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>Lee y borra el resultado dejado por el ayudante, si hay uno.</summary>
    public static UpdateResult? ConsumeResult(UpdatePaths paths)
    {
        try
        {
            if (!File.Exists(paths.ResultFile))
            {
                return null;
            }

            var result = JsonSerializer.Deserialize<UpdateResult>(File.ReadAllText(paths.ResultFile));
            File.Delete(paths.ResultFile);
            if (result is not null && !result.Ok)
            {
                // La versión que arrancó no es la nueva: ya no hay nada pendiente.
                File.Delete(paths.PendingFile);
            }

            return result;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Borra descargas, preparación y respaldos viejos (conserva solo la descarga de <paramref name="keepVersion"/>).</summary>
    public static void Cleanup(UpdatePaths paths, string? keepVersion)
    {
        UpdateInstaller.DeleteDirectory(paths.Staging);
        UpdateInstaller.DeleteDirectory(paths.Helper);
        UpdateInstaller.DeleteDirectory(paths.Backup);
        var downloads = Path.Combine(paths.Root, "downloads");
        try
        {
            if (!Directory.Exists(downloads))
            {
                return;
            }

            foreach (var dir in Directory.GetDirectories(downloads))
            {
                if (!string.Equals(Path.GetFileName(dir), keepVersion, StringComparison.OrdinalIgnoreCase))
                {
                    UpdateInstaller.DeleteDirectory(dir);
                }
            }
        }
        catch
        {
            // ignore
        }
    }
}
