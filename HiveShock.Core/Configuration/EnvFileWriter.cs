using System.Text;

namespace HiveShock.Configuration;

public static class EnvFileWriter
{
    public static void Upsert(string envPath, string key, string value)
    {
        var lines = File.Exists(envPath)
            ? File.ReadAllLines(envPath, Encoding.UTF8).ToList()
            : [];

        var found = false;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i].TrimStart();
            if (line.StartsWith('#') || !line.Contains('='))
            {
                continue;
            }

            var eq = lines[i].IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            if (!string.Equals(lines[i][..eq].Trim(), key, StringComparison.Ordinal))
            {
                continue;
            }

            lines[i] = $"{key}={value}";
            found = true;
            break;
        }

        if (!found)
        {
            if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
            {
                lines.Add("");
            }

            lines.Add($"{key}={value}");
        }

        File.WriteAllLines(envPath, lines, new UTF8Encoding(false));
        Environment.SetEnvironmentVariable(key, value);
    }

    public static string EnsureEnvPath()
    {
        var existing = AppPaths.Find(".env");
        if (existing != null)
        {
            return existing;
        }

        var path = Path.Combine(AppPaths.AppDirectory, ".env");
        var example = AppPaths.Find(".env.example");
        if (example != null)
        {
            // File.Copy usa ioctl FICLONE; Android lo deniega en app data (avc 0x9409).
            File.WriteAllBytes(path, File.ReadAllBytes(example));
        }
        else
        {
            File.WriteAllText(path, "TIKTOK_UNIQUE_ID=\n", new UTF8Encoding(false));
        }

        return path;
    }
}
