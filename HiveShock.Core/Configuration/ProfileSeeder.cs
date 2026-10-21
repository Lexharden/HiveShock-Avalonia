using System.Text.Json;
using HiveShock.Logging;

namespace HiveShock.Configuration;

/// <summary>
/// Instala los perfiles del catálogo que el usuario todavía no tiene, sin tocar los que ya tiene.
/// Las versiones antiguas del actualizador protegen toda la carpeta <c>profiles/</c>, así que los perfiles nuevos viajan en
/// <c>profiles-catalog/</c> (una carpeta que sí se copia) y es esta clase, al arrancar, la que los pasa a <c>profiles/</c>.
/// Lo ya instalado nunca se sobrescribe, y un perfil que el usuario borró a propósito no vuelve.
/// </summary>
public static class ProfileSeeder
{
    public const string CatalogFolder = "profiles-catalog";
    public const string ProfilesFolder = "profiles";
    public const string SeededFile = ".hiveshock-seeded-profiles.json";

    /// <summary>
    /// Copia a <paramref name="profilesDir"/> los perfiles de <paramref name="catalogDir"/> que no estén ya y que no se
    /// hubieran instalado antes. Devuelve los nombres instalados. <paramref name="seeded"/> se actualiza con todo lo visto.
    /// </summary>
    public static List<string> Seed(string catalogDir, string profilesDir, ISet<string> seeded)
    {
        var installed = new List<string>();
        if (!Directory.Exists(catalogDir))
        {
            return installed;
        }

        foreach (var source in Directory.GetDirectories(catalogDir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(source);
            if (!IsComplete(source) || seeded.Contains(name))
            {
                continue;
            }

            var target = Path.Combine(profilesDir, name);
            if (Directory.Exists(target))
            {
                // Ya lo tiene (instalación completa o copiado a mano): se respeta tal cual.
                seeded.Add(name);
                continue;
            }

            var temp = target + ".seeding";
            try
            {
                Directory.CreateDirectory(profilesDir);
                if (Directory.Exists(temp))
                {
                    Directory.Delete(temp, recursive: true);
                }

                CopyDirectory(source, temp);
                Directory.Move(temp, target);
                seeded.Add(name);
                installed.Add(name);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Se reintenta en el siguiente arranque (no se anota como visto).
                BridgeLog.Warn($"Perfil {name}: no se pudo instalar ({ex.Message}).");
                TryDelete(temp);
            }
        }

        return installed;
    }

    /// <summary>Busca <c>profiles-catalog/</c> junto al programa e instala los perfiles que falten. Nunca lanza.</summary>
    public static void SeedFromApp()
    {
        try
        {
            foreach (var root in AppPaths.CandidateRootsPublic())
            {
                var catalog = Path.Combine(root, CatalogFolder);
                if (!Directory.Exists(catalog))
                {
                    continue;
                }

                var seeded = LoadSeeded();
                var installed = Seed(catalog, Path.Combine(root, ProfilesFolder), seeded);
                SaveSeeded(seeded);
                if (installed.Count > 0)
                {
                    BridgeLog.Info($"Perfiles nuevos instalados: {string.Join(", ", installed)}.");
                }

                return;
            }
        }
        catch (Exception ex)
        {
            BridgeLog.Warn($"Perfiles nuevos: {ex.Message}");
        }
    }

    private static bool IsComplete(string dir) =>
        File.Exists(Path.Combine(dir, "profile.json")) &&
        File.Exists(Path.Combine(dir, "effects.json")) &&
        File.Exists(Path.Combine(dir, "gifts.json"));

    private static HashSet<string> LoadSeeded()
    {
        var names = UserDataStore.Load<List<string>>(SeededFile) ?? [];
        return new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
    }

    private static void SaveSeeded(HashSet<string> seeded) =>
        UserDataStore.Save(SeededFile, seeded.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList());

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: false);
        }

        foreach (var dir in Directory.GetDirectories(source))
        {
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
        }
    }

    private static void TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
