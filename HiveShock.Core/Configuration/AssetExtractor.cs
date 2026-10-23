using HiveShock.Logging;

namespace HiveShock.Configuration;

/// <summary>Origen de archivos empaquetados de solo lectura (en Android, los assets del APK).</summary>
public interface IAssetSource
{
    /// <summary>Nombres hijos de <paramref name="path"/>. Vacío si es un archivo, una carpeta vacía o no existe.</summary>
    IReadOnlyList<string> List(string path);

    /// <summary>Abre un archivo, o null si no existe.</summary>
    Stream? Open(string path);
}

/// <summary>
/// Copia assets empaquetados a la carpeta de datos de la app (el APK no se puede escribir).
/// Un asset que no se puede copiar se registra como aviso: ya no se traga en silencio.
/// </summary>
public static class AssetExtractor
{
    /// <summary>Copia <paramref name="assetDir"/> entero a <paramref name="destDir"/>. Devuelve los archivos escritos.</summary>
    public static int CopyTree(IAssetSource source, string assetDir, string destDir, bool overwrite)
    {
        var copied = 0;
        foreach (var name in source.List(assetDir))
        {
            var assetPath = $"{assetDir}/{name}";
            var destPath = Path.Combine(destDir, name);
            if (source.List(assetPath).Count > 0)
            {
                copied += CopyTree(source, assetPath, destPath, overwrite);
            }
            else if (CopyFile(source, assetPath, destPath, overwrite))
            {
                copied++;
            }
        }

        return copied;
    }

    /// <summary>
    /// Copia un archivo. Con <paramref name="overwrite"/> falso no toca uno que ya existe.
    /// Devuelve true si escribió el archivo. Nunca lanza.
    /// </summary>
    public static bool CopyFile(IAssetSource source, string assetPath, string destPath, bool overwrite)
    {
        if (!overwrite && File.Exists(destPath))
        {
            return false;
        }

        var temp = destPath + ".tmp";
        try
        {
            using var input = source.Open(assetPath);
            if (input is null)
            {
                BridgeLog.Warn($"Asset no encontrado: {assetPath}");
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            using (var output = File.Create(temp))
            {
                input.CopyTo(output);
            }

            File.Move(temp, destPath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            BridgeLog.Warn($"Asset {assetPath}: no se pudo copiar ({ex.Message}).");
            try
            {
                File.Delete(temp);
            }
            catch (Exception tempEx) when (tempEx is IOException or UnauthorizedAccessException)
            {
            }

            return false;
        }
    }
}
