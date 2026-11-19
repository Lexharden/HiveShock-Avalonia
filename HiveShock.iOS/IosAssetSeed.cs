using Foundation;
using HiveShock.Configuration;

namespace HiveShock.iOS;

/// <summary>
/// Copia los archivos del bundle (solo lectura) a la carpeta de datos de la app. Igual que en Android:
/// el catálogo de perfiles se renueva con cada versión y lo que el usuario edita solo se copia la primera vez.
/// </summary>
internal static class IosAssetSeed
{
    public static void CopyIfNeeded()
    {
        var destRoot = AppPaths.AppDirectory;
        Directory.CreateDirectory(destRoot);

        var source = new DirectoryAssetSource(NSBundle.MainBundle.BundlePath);

        AssetExtractor.CopyTree(source, ProfileSeeder.CatalogFolder, Path.Combine(destRoot, ProfileSeeder.CatalogFolder), overwrite: true);
        AssetExtractor.CopyFile(source, "tiktok_gifts.json", Path.Combine(destRoot, "tiktok_gifts.json"), overwrite: true);

        AssetExtractor.CopyFile(source, "gift-catalog.json", Path.Combine(destRoot, "gift-catalog.json"), overwrite: false);
        AssetExtractor.CopyFile(source, "active-profile.txt", Path.Combine(destRoot, "active-profile.txt"), overwrite: false);
        AssetExtractor.CopyFile(source, "env.example", Path.Combine(destRoot, ".env.example"), overwrite: false);
    }
}
