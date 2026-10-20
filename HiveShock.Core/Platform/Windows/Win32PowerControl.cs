using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace HiveShock.Platform.Windows;

/// <summary>Reinicio con <c>shutdown.exe</c> (viene con Windows; un usuario normal puede usarlo en su sesión).</summary>
[SupportedOSPlatform("windows")]
public sealed class Win32PowerControl : IPowerControl
{
    public bool IsSupported => true;

    public void Restart(bool force)
    {
        // La cuenta atrás ya la hizo HiveShock: aquí es inmediato (/t 0). «/f» cierra los programas sin preguntar.
        var args = force ? "/r /f /t 0" : "/r /t 0";
        var shutdown = Path.Combine(Environment.SystemDirectory, "shutdown.exe");
        var info = new ProcessStartInfo(shutdown, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };

        try
        {
            using var process = Process.Start(info)
                                ?? throw new InvalidOperationException("No se pudo lanzar shutdown.exe.");
            if (!process.WaitForExit(10_000))
            {
                return; // sigue ejecutándose: el reinicio ya está en marcha
            }

            if (process.ExitCode != 0)
            {
                var error = process.StandardError.ReadToEnd().Trim();
                throw new InvalidOperationException(
                    $"Windows rechazó el reinicio (código {process.ExitCode}){(error.Length > 0 ? ": " + error : "")}");
            }
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException($"No se pudo ejecutar shutdown.exe: {ex.Message}", ex);
        }
    }
}
