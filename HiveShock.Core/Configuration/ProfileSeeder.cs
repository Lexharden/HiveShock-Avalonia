using System.Text.Json;
using System.Text.Json.Nodes;
using HiveShock.Logging;

namespace HiveShock.Configuration;

/// <summary>
/// Instala los perfiles del catálogo que el usuario todavía no tiene, sin tocar los que ya tiene.
/// Las versiones antiguas del actualizador protegen toda la carpeta <c>profiles/</c>, así que los perfiles nuevos viajan en
/// <c>profiles-catalog/</c> (una carpeta que sí se copia) y es esta clase, al arrancar, la que los pasa a <c>profiles/</c>.
/// Lo ya instalado nunca se sobrescribe (salvo <c>effects.json</c>, ver <see cref="SyncEffects"/>), y un perfil que el
/// usuario borró a propósito no vuelve.
/// </summary>
public static class ProfileSeeder
{
    public const string CatalogFolder = "profiles-catalog";
    public const string ProfilesFolder = "profiles";
    public const string SeededFile = ".hiveshock-seeded-profiles.json";
    public const string EffectsSyncFile = ".hiveshock-effects-sync.json";
    public const string EffectsFile = "effects.json";
    public const string EffectsBackupFile = "effects.previous.bak";
    public const string ProfileFile = "profile.json";
    public const string ProfileBackupFile = "profile.previous.bak";

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

    /// <summary>
    /// Mantiene al día el <c>effects.json</c> de los perfiles del catálogo: cuando una versión nueva trae un
    /// <c>effects.json</c> distinto al último que se aplicó, lo reemplaza para que los efectos nuevos de un juego lleguen
    /// solos. Lo anterior se guarda en <c>effects.previous.bak</c>. Mientras el catálogo no cambie, las ediciones del
    /// usuario se respetan. <paramref name="applied"/> (perfil → huella del catálogo aplicado) se actualiza.
    /// Los perfiles que el usuario borró o creó él mismo (que no están en el catálogo) no se tocan.
    /// </summary>
    public static List<string> SyncEffects(string catalogDir, string profilesDir, IDictionary<string, string> applied)
    {
        var updated = new List<string>();
        if (!Directory.Exists(catalogDir))
        {
            return updated;
        }

        foreach (var source in Directory.GetDirectories(catalogDir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(source);
            var targetDir = Path.Combine(profilesDir, name);
            var targetFile = Path.Combine(targetDir, EffectsFile);
            if (!IsComplete(source) || !File.Exists(targetFile))
            {
                continue;
            }

            try
            {
                var catalogText = File.ReadAllText(Path.Combine(source, EffectsFile));
                var catalogHash = Fingerprint(catalogText);
                if (applied.TryGetValue(name, out var last) && last == catalogHash)
                {
                    continue;
                }

                if (Fingerprint(File.ReadAllText(targetFile)) != catalogHash)
                {
                    File.Copy(targetFile, Path.Combine(targetDir, EffectsBackupFile), overwrite: true);
                    AtomicFile.WriteAllText(targetFile, catalogText);
                    updated.Add(name);
                }

                applied[name] = catalogHash;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Se reintenta en el siguiente arranque (no se anota como aplicado).
                BridgeLog.Warn($"Perfil {name}: no se pudo actualizar effects.json ({ex.Message}).");
            }
        }

        return updated;
    }

    /// <summary>
    /// Complementa <c>effects.json</c>: añade al <c>profile.json</c> instalado los <c>labels</c> y los <c>enemyEffects</c>
    /// que el catálogo trae y al usuario le faltan, para que los efectos nuevos salgan con su nombre y en su grupo.
    /// Es una fusión que solo suma: no cambia lo que el usuario ya tiene (nombres editados, puertos, etc.) y se hace una
    /// vez por cada versión nueva del <c>profile.json</c> del catálogo. Lo anterior queda en <c>profile.previous.bak</c>.
    /// </summary>
    public static List<string> SyncProfileMeta(string catalogDir, string profilesDir, IDictionary<string, string> applied)
    {
        var updated = new List<string>();
        if (!Directory.Exists(catalogDir))
        {
            return updated;
        }

        foreach (var source in Directory.GetDirectories(catalogDir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(source);
            var targetDir = Path.Combine(profilesDir, name);
            var targetFile = Path.Combine(targetDir, ProfileFile);
            var key = name + "/" + ProfileFile;
            if (!IsComplete(source) || !File.Exists(targetFile))
            {
                continue;
            }

            try
            {
                var catalogText = File.ReadAllText(Path.Combine(source, ProfileFile));
                var catalogHash = Fingerprint(catalogText);
                if (applied.TryGetValue(key, out var last) && last == catalogHash)
                {
                    continue;
                }

                var installedText = File.ReadAllText(targetFile);
                if (MergeMeta(catalogText, installedText) is { } merged)
                {
                    File.Copy(targetFile, Path.Combine(targetDir, ProfileBackupFile), overwrite: true);
                    AtomicFile.WriteAllText(targetFile, merged);
                    updated.Add(name);
                }

                applied[key] = catalogHash;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                BridgeLog.Warn($"Perfil {name}: no se pudo actualizar profile.json ({ex.Message}).");
            }
            catch (JsonException ex)
            {
                // Un profile.json ilegible lo gestiona el cargador de perfiles; aquí solo se salta.
                BridgeLog.Warn($"Perfil {name}: profile.json no se pudo fusionar ({ex.Message}).");
                applied[key] = Fingerprint(File.ReadAllText(Path.Combine(source, ProfileFile)));
            }
        }

        return updated;
    }

    /// <summary>Devuelve el profile.json instalado con lo que le falta del catálogo, o null si ya lo tiene todo.</summary>
    private static string? MergeMeta(string catalogText, string installedText)
    {
        var docOptions = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        if (JsonNode.Parse(catalogText, documentOptions: docOptions) is not JsonObject catalog ||
            JsonNode.Parse(installedText, documentOptions: docOptions) is not JsonObject installed)
        {
            return null;
        }

        var changed = false;

        if (catalog["labels"] is JsonObject catalogLabels)
        {
            if (installed["labels"] is not JsonObject labels)
            {
                labels = new JsonObject();
                installed["labels"] = labels;
            }

            var have = new HashSet<string>(labels.Select(p => p.Key), StringComparer.OrdinalIgnoreCase);
            foreach (var (id, text) in catalogLabels)
            {
                if (have.Add(id))
                {
                    labels[id] = text?.DeepClone();
                    changed = true;
                }
            }
        }

        if (catalog["enemyEffects"] is JsonArray catalogEnemies)
        {
            if (installed["enemyEffects"] is not JsonArray enemies)
            {
                enemies = new JsonArray();
                installed["enemyEffects"] = enemies;
            }

            var have = new HashSet<string>(
                enemies.Select(n => n?.GetValue<string>() ?? ""), StringComparer.OrdinalIgnoreCase);
            foreach (var id in catalogEnemies.Select(n => n?.GetValue<string>()).OfType<string>())
            {
                if (have.Add(id))
                {
                    enemies.Add(id);
                    changed = true;
                }
            }
        }

        return changed
            ? installed.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }) + Environment.NewLine
            : null;
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

                var applied = LoadEffectsSync();
                var profilesDir = Path.Combine(root, ProfilesFolder);
                var updated = SyncEffects(catalog, profilesDir, applied);
                var meta = SyncProfileMeta(catalog, profilesDir, applied);
                UserDataStore.Save(EffectsSyncFile, applied);
                if (updated.Count > 0)
                {
                    BridgeLog.Info($"Efectos actualizados: {string.Join(", ", updated)} (copia anterior en {EffectsBackupFile}).");
                }

                if (meta.Count > 0)
                {
                    BridgeLog.Info($"Nombres y enemigos nuevos añadidos a: {string.Join(", ", meta)}.");
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

    private static Dictionary<string, string> LoadEffectsSync() =>
        new(UserDataStore.Load<Dictionary<string, string>>(EffectsSyncFile) ?? [], StringComparer.OrdinalIgnoreCase);

    /// <summary>Huella del contenido sin depender de los saltos de línea (CRLF/LF).</summary>
    private static string Fingerprint(string text) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n"))));

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
