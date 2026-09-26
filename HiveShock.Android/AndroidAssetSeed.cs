using Android.Content.Res;
using HiveShock.Configuration;

namespace HiveShock.Android;

/// <summary>Copia perfiles embebidos a datos de la app (el APK no se puede escribir).</summary>
internal static class AndroidAssetSeed
{
    public static void CopyIfNeeded()
    {
        var destRoot = AppPaths.AppDirectory;
        Directory.CreateDirectory(destRoot);

        var assets = global::Android.App.Application.Context.Assets
                     ?? throw new InvalidOperationException("No hay Assets de Android.");

        CopyMissingTree(assets, "profiles", Path.Combine(destRoot, "profiles"));
        CopyFileIfMissing(assets, "gift-catalog.json", Path.Combine(destRoot, "gift-catalog.json"));
        // Lista oficial de regalos: no es del usuario, se renueva con cada versión de la app.
        CopyFileAlways(assets, "tiktok_gifts.json", Path.Combine(destRoot, "tiktok_gifts.json"));
        CopyFileIfMissing(assets, "active-profile.txt", Path.Combine(destRoot, "active-profile.txt"));
        CopyFileIfMissing(assets, "env.example", Path.Combine(destRoot, ".env.example"));
    }

    private static void CopyMissingTree(AssetManager assets, string assetDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        string[]? names;
        try
        {
            names = assets.List(assetDir);
        }
        catch
        {
            return;
        }

        if (names is null || names.Length == 0)
        {
            return;
        }

        foreach (var name in names)
        {
            var assetPath = string.IsNullOrEmpty(assetDir) ? name : $"{assetDir}/{name}";
            var destPath = Path.Combine(destDir, name);
            if (IsDirectory(assets, assetPath))
            {
                if (assetDir == "profiles" && Directory.Exists(destPath))
                {
                    continue;
                }

                CopyMissingTree(assets, assetPath, destPath);
            }
            else
            {
                CopyFileIfMissing(assets, assetPath, destPath);
            }
        }
    }

    private static bool IsDirectory(AssetManager assets, string assetPath)
    {
        try
        {
            using var stream = assets.Open(assetPath);
            return false;
        }
        catch
        {
            return true;
        }
    }

    private static void CopyFileAlways(AssetManager assets, string assetPath, string destPath)
    {
        try
        {
            using var input = assets.Open(assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            using var output = File.Create(destPath);
            input.CopyTo(output);
        }
        catch
        {
            // Asset opcional
        }
    }

    private static void CopyFileIfMissing(AssetManager assets, string assetPath, string destPath)
    {
        if (File.Exists(destPath))
        {
            return;
        }

        try
        {
            using var input = assets.Open(assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            using var output = File.Create(destPath);
            input.CopyTo(output);
        }
        catch
        {
            // Asset opcional
        }
    }
}
