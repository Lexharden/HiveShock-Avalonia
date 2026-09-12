using System.Text;
using HiveShock.Configuration;

namespace HiveShock.Logging;

public enum LogLevel
{
    Info,
    Warn,
    Error,
}

public sealed record LogEntry(DateTime Timestamp, LogLevel Level, string Message)
{
    public string Formatted => $"{Timestamp:HH:mm:ss} [{Level.ToString().ToUpperInvariant()}] {Message}";
}

/// <summary>Logs a consola + archivo diario. Emite <see cref="Logged"/> para la UI.</summary>
public static class BridgeLog
{
    private static readonly object Gate = new();
    private static StreamWriter? _writer;
    private static string _path = "";

    public static string Path => _path;

    public static event Action<LogEntry>? Logged;

    public static void Init()
    {
        lock (Gate)
        {
            if (_writer != null)
            {
                return;
            }

            var dir = System.IO.Path.Combine(AppPaths.AppDirectory, "logs");
            Directory.CreateDirectory(dir);
            _path = System.IO.Path.Combine(dir, $"HiveShock-{DateTime.Now:yyyyMMdd}.log");
            _writer = new StreamWriter(
                new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
                new UTF8Encoding(false))
            {
                AutoFlush = true,
            };
        }
    }

    public static void Info(string message) => Write(LogLevel.Info, message);

    public static void Warn(string message) => Write(LogLevel.Warn, message);

    public static void Error(string message) => Write(LogLevel.Error, message);

    public static void Close()
    {
        lock (Gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    private static void Write(LogLevel level, string message)
    {
        var entry = new LogEntry(DateTime.Now, level, message);
        var line = $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss} [{level.ToString().ToUpperInvariant()}] {message}";

        try
        {
            if (level == LogLevel.Error)
            {
                Console.Error.WriteLine(line);
            }
            else
            {
                Console.Out.WriteLine(line);
            }
        }
        catch
        {
            // consola puede no existir en WinExe
        }

        lock (Gate)
        {
            try
            {
                _writer?.WriteLine(line);
            }
            catch
            {
                // no romper el bridge por fallo de disco
            }
        }

        try
        {
            Logged?.Invoke(entry);
        }
        catch
        {
            // no romper por handlers de UI
        }
    }
}
