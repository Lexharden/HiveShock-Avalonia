using Android.Content.Res;
using HiveShock.Configuration;
using HiveShock.Logging;

namespace HiveShock.Android;

/// <summary>
/// Copia los assets del APK a los datos de la app (el APK no se puede escribir).
/// Los perfiles viajan como catálogo (<c>profiles-catalog/</c>) y los instala <see cref="ProfileSeeder"/>,
/// igual que en escritorio: no pisa los del usuario y respeta los que borró a propósito.
/// </summary>
internal static class AndroidAssetSeed
{
    public static void CopyIfNeeded()
    {
        var destRoot = AppPaths.AppDirectory;
        Directory.CreateDirectory(destRoot);

        var manager = global::Android.App.Application.Context.Assets
                      ?? throw new InvalidOperationException("No hay Assets de Android.");
        var source = new AssetManagerSource(manager);

        // El catálogo es de la app, no del usuario: se renueva con cada versión.
        AssetExtractor.CopyTree(source, ProfileSeeder.CatalogFolder, Path.Combine(destRoot, ProfileSeeder.CatalogFolder), overwrite: true);
        AssetExtractor.CopyFile(source, "tiktok_gifts.json", Path.Combine(destRoot, "tiktok_gifts.json"), overwrite: true);

        // Lo que el usuario edita: solo la primera vez.
        AssetExtractor.CopyFile(source, "gift-catalog.json", Path.Combine(destRoot, "gift-catalog.json"), overwrite: false);
        AssetExtractor.CopyFile(source, "active-profile.txt", Path.Combine(destRoot, "active-profile.txt"), overwrite: false);
        AssetExtractor.CopyFile(source, "env.example", Path.Combine(destRoot, ".env.example"), overwrite: false);
    }

    private sealed class AssetManagerSource(AssetManager assets) : IAssetSource
    {
        public IReadOnlyList<string> List(string path)
        {
            try
            {
                return assets.List(path) ?? [];
            }
            catch (Exception ex) when (ex is Java.IO.IOException)
            {
                BridgeLog.Warn($"Assets {path}: {ex.Message}");
                return [];
            }
        }

        public Stream? Open(string path)
        {
            try
            {
                return assets.Open(path);
            }
            catch (Java.IO.FileNotFoundException)
            {
                return null;
            }
        }
    }
}
