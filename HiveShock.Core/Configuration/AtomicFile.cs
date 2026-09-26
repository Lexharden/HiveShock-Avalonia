using System.Text;

namespace HiveShock.Configuration;

/// <summary>
/// Escritura segura de archivos de datos: se escribe a un temporal y se reemplaza el original,
/// dejando el anterior como <c>.bak</c>. Un cierre a mitad de guardado nunca deja un archivo
/// roto con el nombre bueno. Lo usan <see cref="UserDataStore"/> y <see cref="GiftCatalogStore"/>.
/// </summary>
public static class AtomicFile
{
    /// <summary>Escribe el texto (UTF-8 sin BOM). Lanza si no se pudo; el temporal se limpia.</summary>
    public static void WriteAllText(string path, string content)
    {
        var temp = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
            {
                try
                {
                    File.Replace(temp, path, path + ".bak", ignoreMetadataErrors: true);
                }
                catch (Exception)
                {
                    // Algunos antivirus o discos no permiten Replace: copia manual del respaldo.
                    File.Copy(path, path + ".bak", overwrite: true);
                    File.Move(temp, path, overwrite: true);
                }
            }
            else
            {
                File.Move(temp, path);
            }
        }
        catch
        {
            try
            {
                File.Delete(temp);
            }
            catch
            {
                // nada que limpiar
            }

            throw;
        }
    }
}
