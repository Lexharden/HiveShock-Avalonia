using System.Globalization;
using System.Text;
using System.Text.Json;

namespace HiveShock.Configuration;

/// <summary>Cómo arranca un perfil propio nuevo.</summary>
public enum ProfileTemplate
{
    /// <summary>Sin efectos de juego ni regalos: solo comandos de chat y macros de teclas.</summary>
    Blank,

    /// <summary>Como «en blanco», con un comando de ejemplo ya enlazado a una macro.</summary>
    Starter,

    /// <summary>Copia de otro perfil (efectos, regalos y comandos).</summary>
    CopyOf,
}

/// <summary>
/// Perfiles creados por el usuario. Viven en <see cref="ProfileStore.UserProfilesRoot"/> (su carpeta de datos),
/// así que ninguna actualización los toca, y se pueden crear, copiar y borrar sin afectar al catálogo de juegos.
/// </summary>
public static class CustomProfileStore
{
    public const int MaxNameLength = 60;
    public const string StarterWord = "saltar";

    private static readonly JsonSerializerOptions ProfileJson = new(JsonDefaults.Options)
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static LoadedGameProfile Create(
        string displayName,
        ProfileTemplate template,
        LoadedGameProfile? source = null,
        string? starterMacroId = null)
    {
        var taken = ProfileStore.ListProfiles().Select(p => p.Id).ToList();
        var dir = Create(ProfileStore.UserProfilesRoot, taken, displayName, template, source, starterMacroId);
        return ProfileStore.LoadFromDirectory(dir);
    }

    /// <summary>Crea la carpeta del perfil bajo <paramref name="root"/> y devuelve su ruta.</summary>
    public static string Create(
        string root,
        IReadOnlyCollection<string> takenIds,
        string displayName,
        ProfileTemplate template,
        LoadedGameProfile? source = null,
        string? starterMacroId = null)
    {
        var name = (displayName ?? "").Trim();
        if (name.Length == 0)
        {
            throw new ArgumentException("Ponle un nombre al perfil.");
        }

        if (name.Length > MaxNameLength)
        {
            throw new ArgumentException($"El nombre es demasiado largo (máximo {MaxNameLength} letras).");
        }

        if (template == ProfileTemplate.CopyOf && source == null)
        {
            throw new ArgumentException("Falta el perfil que se quiere copiar.");
        }

        var id = MakeId(name, takenIds, root);
        var dir = Path.Combine(root, id);
        try
        {
            Directory.CreateDirectory(dir);
            var info = template == ProfileTemplate.CopyOf ? CloneInfo(source!.Info) : NewInfo();
            info.Id = id;
            info.DisplayName = name;
            info.Window = null;
            if (template != ProfileTemplate.CopyOf)
            {
                info.Description = "Perfil propio: comandos y macros, sin juego del catálogo.";
            }
            else if (string.IsNullOrWhiteSpace(info.Description))
            {
                info.Description = $"Copia de {source!.ShortDisplayName}.";
            }

            AtomicFile.WriteAllText(Path.Combine(dir, "profile.json"), JsonSerializer.Serialize(info, ProfileJson) + Environment.NewLine);

            if (template == ProfileTemplate.CopyOf)
            {
                AtomicFile.WriteAllText(Path.Combine(dir, "effects.json"), File.ReadAllText(source!.EffectsPath, Encoding.UTF8));
                AtomicFile.WriteAllText(Path.Combine(dir, "gifts.json"), File.ReadAllText(source.GiftsPath, Encoding.UTF8));
            }
            else
            {
                AtomicFile.WriteAllText(Path.Combine(dir, "effects.json"), "{}" + Environment.NewLine);
                var starter = template == ProfileTemplate.Starter ? starterMacroId : null;
                AtomicFile.WriteAllText(Path.Combine(dir, "gifts.json"), BlankGifts(starter));
            }
        }
        catch
        {
            TryDeleteDirectory(dir);
            throw;
        }

        return dir;
    }

    /// <summary>
    /// Quita un perfil propio. No se borra del disco: se mueve a <c>profiles-deleted</c> por si fue un error.
    /// Los perfiles del catálogo no se pueden quitar.
    /// </summary>
    public static string Delete(LoadedGameProfile profile)
    {
        if (!profile.IsCustom)
        {
            throw new InvalidOperationException("Solo se pueden borrar los perfiles propios.");
        }

        var trash = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(ProfileStore.UserProfilesRoot))!, "profiles-deleted");
        Directory.CreateDirectory(trash);
        var target = Path.Combine(trash, $"{profile.Id}-{DateTime.Now:yyyyMMdd-HHmmss}");
        Directory.Move(profile.Directory, target);
        return target;
    }

    /// <summary>Id corto y seguro para carpeta: «Mi stream (Minecraft)» → «mi-stream-minecraft», único.</summary>
    public static string MakeId(string name, IEnumerable<string> takenIds, string? root = null)
    {
        var decomposed = name.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                sb.Append(c);
            }
            else if (c is >= 'A' and <= 'Z')
            {
                sb.Append(char.ToLowerInvariant(c));
            }
            else if (sb.Length > 0 && sb[^1] != '-')
            {
                sb.Append('-');
            }
        }

        var slug = sb.ToString().Trim('-');
        if (slug.Length == 0)
        {
            slug = "perfil";
        }

        if (slug.Length > 40)
        {
            slug = slug[..40].TrimEnd('-');
        }

        var taken = new HashSet<string>(takenIds, StringComparer.OrdinalIgnoreCase);
        var candidate = slug;
        for (var n = 2; taken.Contains(candidate) || (root != null && Directory.Exists(Path.Combine(root, candidate))); n++)
        {
            candidate = $"{slug}-{n}";
        }

        return candidate;
    }

    private static GameProfileInfo NewInfo() => new()
    {
        // Sin juego: estos valores solo se usarían si algún efecto de juego se añadiera a mano.
        SupportsDeathEvents = false,
        SupportsDeleteSave = false,
        SupportsRescue = false,
    };

    private static GameProfileInfo CloneInfo(GameProfileInfo info) =>
        JsonSerializer.Deserialize<GameProfileInfo>(JsonSerializer.Serialize(info, ProfileJson), ProfileJson)
        ?? NewInfo();

    private static string BlankGifts(string? starterMacroId)
    {
        var commands = string.IsNullOrWhiteSpace(starterMacroId)
            ? "{}"
            : $"{{ {JsonSerializer.Serialize(StarterWord)}: {JsonSerializer.Serialize(starterMacroId)} }}";
        return $$"""
            {
              "gifts": [],
              "likes": { "every": 0, "effect": "", "what": "" },
              "follow": { "effect": "", "what": "" },
              "share": { "effect": "", "what": "" },
              "chat": {
                "enabled": true,
                "prefix": "!",
                "cooldownSec": 30,
                "globalGapSec": 2,
                "commands": {{commands}}
              },
              "unmappedGifts": "ignore",
              "giftsByDiamonds": []
            }
            """ + Environment.NewLine;
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (IOException)
        {
            // un resto vacío no impide crear otro perfil
        }
    }
}
