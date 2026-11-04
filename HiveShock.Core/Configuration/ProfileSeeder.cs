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

    /// <summary>Valores del catálogo que se sembraron sin que el usuario los cambiara; permiten actualizarlos solo si siguen intactos.</summary>
    public const string EffectsBaseFile = ".hiveshock-effects-base.json";
    public const string LabelsBaseFile = ".hiveshock-labels-base.json";

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
    /// <c>effects.json</c> distinto al último que se aplicó, añade los efectos que el usuario no tiene para que los efectos
    /// nuevos de un juego lleguen solos. Los que ya tiene se actualizan solo si siguen idénticos a lo sembrado
    /// (<see cref="EffectsBaseFile"/>); los editados o propios no se tocan, y nunca se quitan. Lo anterior se guarda en <c>effects.previous.bak</c>. Se hace una vez por cada versión del
    /// catálogo. <paramref name="applied"/> (perfil → huella del catálogo aplicado) se actualiza.
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
                var baseFile = Path.Combine(targetDir, EffectsBaseFile);
                if (applied.TryGetValue(name, out var last) && last == catalogHash && File.Exists(baseFile))
                {
                    continue;
                }

                if (MergeEffects(catalogText, File.ReadAllText(targetFile), ReadBaseline(baseFile)) is { } result)
                {
                    if (result.Text is { } merged)
                    {
                        File.Copy(targetFile, Path.Combine(targetDir, EffectsBackupFile), overwrite: true);
                        AtomicFile.WriteAllText(targetFile, merged);
                        updated.Add(name);
                    }

                    SaveBaseline(baseFile, result.Baseline);
                }

                applied[name] = catalogHash;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Se reintenta en el siguiente arranque (no se anota como aplicado).
                BridgeLog.Warn($"Perfil {name}: no se pudo actualizar effects.json ({ex.Message}).");
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
            {
                // Un effects.json ilegible lo gestiona el cargador de perfiles; aquí solo se salta (y no se reintenta en bucle).
                BridgeLog.Warn($"Perfil {name}: effects.json no se pudo fusionar ({ex.Message}).");
                applied[name] = SafeFingerprint(Path.Combine(source, EffectsFile));
            }
        }

        return updated;
    }

    /// <summary>
    /// Complementa <c>effects.json</c>: añade al <c>profile.json</c> instalado los <c>labels</c>, <c>enemyEffects</c> y <c>singletonEffects</c>
    /// que el catálogo trae y al usuario le faltan, para que los efectos nuevos salgan con su nombre y en su grupo.
    /// Un nombre ya existente solo se actualiza si el usuario no lo cambió (<see cref="LabelsBaseFile"/>); puertos y demás no se tocan. Se hace una
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
                var baseFile = Path.Combine(targetDir, LabelsBaseFile);
                if (applied.TryGetValue(key, out var last) && last == catalogHash && File.Exists(baseFile))
                {
                    continue;
                }

                var installedText = File.ReadAllText(targetFile);
                var labelsBaseline = ReadBaseline(baseFile);
                AddKnownSeededLabels(name, labelsBaseline);
                if (MergeMeta(catalogText, installedText, labelsBaseline) is { } result)
                {
                    if (result.Text is { } merged)
                    {
                        File.Copy(targetFile, Path.Combine(targetDir, ProfileBackupFile), overwrite: true);
                        AtomicFile.WriteAllText(targetFile, merged);
                        updated.Add(name);
                    }

                    SaveBaseline(baseFile, result.Baseline);
                }

                applied[key] = catalogHash;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                BridgeLog.Warn($"Perfil {name}: no se pudo actualizar profile.json ({ex.Message}).");
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
            {
                // Un profile.json ilegible lo gestiona el cargador de perfiles; aquí solo se salta (y no se reintenta en bucle).
                BridgeLog.Warn($"Perfil {name}: profile.json no se pudo fusionar ({ex.Message}).");
                applied[key] = SafeFingerprint(Path.Combine(source, ProfileFile));
            }
        }

        return updated;
    }

    /// <summary>Resultado de una fusión: el texto nuevo (null si no hubo cambios) y los valores del catálogo que quedaron intactos.</summary>
    private sealed record MergeResult(string? Text, JsonObject Baseline);

    private static readonly JsonDocumentOptions DocOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Fusiona el effects.json del catálogo en el instalado: añade los efectos que faltan y actualiza solo los que el
    /// usuario no ha cambiado desde que se sembraron (iguales a <paramref name="baseline"/>). Null si algún archivo no es un objeto.
    /// </summary>
    private static MergeResult? MergeEffects(string catalogText, string installedText, JsonObject baseline)
    {
        if (JsonNode.Parse(catalogText, documentOptions: DocOptions) is not JsonObject catalog ||
            JsonNode.Parse(installedText, documentOptions: DocOptions) is not JsonObject installed)
        {
            return null;
        }

        var changed = MergeEntries(catalog, installed, baseline, out var newBaseline);
        return new MergeResult(changed ? installed.ToJsonString(WriteOptions) + Environment.NewLine : null, newBaseline);
    }

    /// <summary>
    /// Fusiona <c>labels</c>, <c>enemyEffects</c> y <c>singletonEffects</c> del profile.json del catálogo en el instalado.
    /// Lo que falta se añade; un nombre ya existente solo se actualiza si el usuario no lo cambió (igual a <paramref name="labelsBaseline"/>).
    /// Una sección con un tipo inesperado se deja como está.
    /// </summary>
    private static MergeResult? MergeMeta(string catalogText, string installedText, JsonObject labelsBaseline)
    {
        if (JsonNode.Parse(catalogText, documentOptions: DocOptions) is not JsonObject catalog ||
            JsonNode.Parse(installedText, documentOptions: DocOptions) is not JsonObject installed)
        {
            return null;
        }

        var changed = false;
        var newBaseline = new JsonObject();

        if (catalog["labels"] is JsonObject catalogLabels)
        {
            var node = installed["labels"];
            if (node is null)
            {
                node = new JsonObject();
                installed["labels"] = node;
            }

            if (node is JsonObject labels)
            {
                changed |= MergeEntries(catalogLabels, labels, labelsBaseline, out newBaseline);
            }
        }

        changed |= MergeStringList(catalog, installed, "enemyEffects");
        changed |= MergeStringList(catalog, installed, "singletonEffects");

        return new MergeResult(changed ? installed.ToJsonString(WriteOptions) + Environment.NewLine : null, newBaseline);
    }

    /// <summary>
    /// Añade a <paramref name="installed"/> las entradas del catálogo que no tiene y reemplaza las que siguen idénticas a lo
    /// sembrado antes (<paramref name="baseline"/>) y han cambiado en el catálogo. Lo editado por el usuario no se toca.
    /// <paramref name="newBaseline"/> recoge las entradas que hoy coinciden con el catálogo.
    /// </summary>
    private static bool MergeEntries(JsonObject catalog, JsonObject installed, JsonObject baseline, out JsonObject newBaseline)
    {
        var keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in installed)
        {
            keys.TryAdd(property.Key, property.Key);
        }

        newBaseline = new JsonObject();
        var changed = false;
        foreach (var (id, definition) in catalog)
        {
            if (!keys.TryGetValue(id, out var key))
            {
                installed[id] = definition?.DeepClone();
                keys[id] = id;
                newBaseline[id] = definition?.DeepClone();
                changed = true;
                continue;
            }

            var current = installed[key];
            if (JsonNode.DeepEquals(current, definition))
            {
                newBaseline[id] = definition?.DeepClone();
            }
            else if (baseline.TryGetPropertyValue(id, out var seeded) && JsonNode.DeepEquals(current, seeded))
            {
                installed[key] = definition?.DeepClone();
                newBaseline[id] = definition?.DeepClone();
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>Suma a una lista de textos (<c>enemyEffects</c>, <c>singletonEffects</c>) los del catálogo que faltan. Solo suma.</summary>
    private static bool MergeStringList(JsonObject catalog, JsonObject installed, string property)
    {
        if (catalog[property] is not JsonArray source)
        {
            return false;
        }

        var node = installed[property];
        if (node is null)
        {
            node = new JsonArray();
            installed[property] = node;
        }

        if (node is not JsonArray target)
        {
            return false;
        }

        var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in target)
        {
            if (AsString(item) is { } text)
            {
                have.Add(text);
            }
        }

        var changed = false;
        foreach (var item in source)
        {
            if (AsString(item) is { } id && have.Add(id))
            {
                target.Add(id);
                changed = true;
            }
        }

        return changed;
    }

    private static string? AsString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    /// <summary>
    /// Nombres que versiones anteriores sembraron y que aún no tenían copia de lo sembrado. Si el usuario conserva exactamente
    /// ese valor, se sabe que no lo editó y puede actualizarse. Solo para cambios de nombre anteriores a <see cref="LabelsBaseFile"/>.
    /// </summary>
    private static readonly Dictionary<string, Dictionary<string, string>> KnownSeededLabels =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["ocarina-of-time"] = new(StringComparer.OrdinalIgnoreCase) { ["fairy"] = "Hada" },
        };

    private static void AddKnownSeededLabels(string profile, JsonObject baseline)
    {
        if (!KnownSeededLabels.TryGetValue(profile, out var known))
        {
            return;
        }

        foreach (var (id, text) in known)
        {
            if (!baseline.ContainsKey(id))
            {
                baseline[id] = text;
            }
        }
    }

    /// <summary>Lee lo sembrado antes; vacío si no existe o no se puede leer (entonces lo que difiera se trata como del usuario).</summary>
    private static JsonObject ReadBaseline(string path)
    {
        try
        {
            if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path), documentOptions: DocOptions) is JsonObject baseline)
            {
                return baseline;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }

        return new JsonObject();
    }

    private static void SaveBaseline(string path, JsonObject baseline)
    {
        var text = baseline.ToJsonString(WriteOptions) + Environment.NewLine;
        if (File.Exists(path) && File.ReadAllText(path) == text)
        {
            return;
        }

        AtomicFile.WriteAllText(path, text);
    }

    private static string SafeFingerprint(string path)
    {
        try
        {
            return Fingerprint(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
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
