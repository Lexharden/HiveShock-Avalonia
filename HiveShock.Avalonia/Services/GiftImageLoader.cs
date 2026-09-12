using Avalonia.Media.Imaging;
using HiveShock.Configuration;

namespace HiveShock.Avalonia.Services;

public static class GiftImageLoader
{
    private static readonly Dictionary<string, Bitmap> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static Bitmap? Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        lock (Cache)
        {
            if (Cache.TryGetValue(path, out var hit))
            {
                return hit;
            }

            try
            {
                var bmp = new Bitmap(path);
                Cache[path] = bmp;
                return bmp;
            }
            catch
            {
                return null;
            }
        }
    }

    public static Bitmap? LoadFor(string? id, string? name, string? image = null) =>
        Load(GiftImages.ResolvePath(id, name, image));

    public static void ClearCache()
    {
        lock (Cache)
        {
            foreach (var bmp in Cache.Values)
            {
                bmp.Dispose();
            }

            Cache.Clear();
        }
    }
}
