using System.Diagnostics;

namespace HiveShock;

public static class PlatformShell
{
    public static void OpenFolder(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true,
            });
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "open",
                ArgumentList = { path },
            })?.Dispose();
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "xdg-open",
            ArgumentList = { path },
        })?.Dispose();
    }

    public static void OpenFile(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "notepad.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true,
            });
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "open",
                ArgumentList = { "-t", path },
            })?.Dispose();
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "xdg-open",
            ArgumentList = { path },
        })?.Dispose();
    }

    public static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true,
        })?.Dispose();
    }
}
