using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HiveShock.Input;

namespace HiveShock.Configuration;

/// <summary>Producto / branding estable (independiente del juego activo).</summary>
public static class ProductInfo
{
    public const string Name = "HiveShock";
    public const string Tagline = "Lo que pasa en tu live llega al juego";
    public const string Vendor = "Yafel";
    public const string Website = "https://hiveshock.yafel.dev";

    public static string ExecutableFileName =>
        OperatingSystem.IsWindows() ? $"{Name}.exe" : Name;
}

/// <summary>App de Twitch embebida. El Client ID es público; el usuario no lo ve.</summary>
public static class TwitchApp
{
    public const string ClientId = "";
}

/// <summary>Metadatos de un perfil de juego (efectos + regalos + puertos).</summary>
public sealed class GameProfileInfo
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public string GameHost { get; set; } = "127.0.0.1";
    public int GamePort { get; set; } = 43000;
    public int CrowdControlPort { get; set; } = 43001;
    public int EventPort { get; set; } = 43002;
    public string Protocol { get; set; } = "json-line";
    public bool SupportsDeathEvents { get; set; } = true;
    public bool SupportsDeleteSave { get; set; }
    public bool SupportsRescue { get; set; }
    public Dictionary<string, string> Labels { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> EnemyEffects { get; set; } = [];
    public List<string> SingletonEffects { get; set; } = [];
    public string HelpNotes { get; set; } = "";

    /// <summary>Ventana del juego a la que se envían las teclas de las macros (keys.json).</summary>
    public WindowTarget? Window { get; set; }

    public string Label(string key, string fallback)
    {
        if (Labels.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return fallback;
    }
}

/// <summary>Perfil resuelto con rutas absolutas a effects/gifts.</summary>
public sealed class LoadedGameProfile
{
    public required GameProfileInfo Info { get; init; }
    public required string Directory { get; init; }
    public required string EffectsPath { get; init; }
    public required string GiftsPath { get; init; }
    public required string ProfileJsonPath { get; init; }

    /// <summary>Perfil creado por el usuario (carpeta de datos del usuario): se puede borrar y no lo pisa una actualización.</summary>
    public bool IsCustom { get; init; }

    public string KeysPath => Path.Combine(Directory, KeyMacroCatalogStore.FileName);

    public string Id => Info.Id;
    public string DisplayName => Info.DisplayName;

    /// <summary>Nombre corto sin el motor/mod entre paréntesis.</summary>
    public string ShortDisplayName
    {
        get
        {
            var name = DisplayName;
            var paren = name.IndexOf('(');
            return paren > 0 ? name[..paren].Trim() : name;
        }
    }
}

public static class ProfileStore
{
    public const string DefaultProfileId = "majora-mask";
    private const string ActiveFileName = "active-profile.txt";

    public static string ProfilesRoot =>
        FindProfilesRoot()
        ?? throw new DirectoryNotFoundException(
            $"No se encontró la carpeta profiles/ (carpeta: {AppPaths.AppDirectory}).");

    /// <summary>Perfiles propios del usuario. Viven en su carpeta de datos, no junto al programa.</summary>
    public static string UserProfilesRoot => Path.Combine(UserDataStore.UserDirectory, "profiles");

    public static IReadOnlyList<LoadedGameProfile> ListProfiles()
    {
        var list = new List<LoadedGameProfile>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // El catálogo (junto al programa) va primero y gana si un id se repite; después, los perfiles propios.
        foreach (var root in new[] { FindProfilesRoot(), UserProfilesRoot })
        {
            if (root != null && Directory.Exists(root) && seenRoots.Add(Path.GetFullPath(root)))
            {
                AddProfilesFrom(root, list, seenIds);
            }
        }

        if (list.Count == 0)
        {
            throw new InvalidOperationException(
                $"No hay perfiles válidos en {FindProfilesRoot() ?? AppPaths.AppDirectory}. " +
                "Cada perfil necesita profile.json, effects.json y gifts.json.");
        }

        return list;
    }

    private static void AddProfilesFrom(string root, List<LoadedGameProfile> list, HashSet<string> seenIds)
    {
        foreach (var dir in Directory.GetDirectories(root).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var profilePath = Path.Combine(dir, "profile.json");
            var effectsPath = Path.Combine(dir, "effects.json");
            var giftsPath = Path.Combine(dir, "gifts.json");
            if (!File.Exists(profilePath) || !File.Exists(effectsPath) || !File.Exists(giftsPath))
            {
                continue;
            }

            try
            {
                var loaded = LoadFromDirectory(dir);
                if (seenIds.Add(loaded.Id))
                {
                    list.Add(loaded);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[HiveShock] Perfil inválido en {dir}: {ex.Message}");
            }
        }
    }

    public static LoadedGameProfile LoadActive(string? preferredId = null)
    {
        var profiles = ListProfiles();
        var id = NormalizeId(preferredId)
                 ?? ReadSavedActiveId()
                 ?? Environment.GetEnvironmentVariable("CROWDBRIDGE_PROFILE")
                 ?? Environment.GetEnvironmentVariable("BRIDGE_PROFILE")
                 ?? DefaultProfileId;

        var match = profiles.FirstOrDefault(p =>
            string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        if (match != null)
        {
            return match;
        }

        match = profiles.FirstOrDefault(p =>
            string.Equals(p.Id, DefaultProfileId, StringComparison.OrdinalIgnoreCase));
        return match ?? profiles[0];
    }

    public static LoadedGameProfile LoadById(string profileId)
    {
        var id = NormalizeId(profileId) ?? throw new ArgumentException("Id de perfil vacío.");
        var match = ListProfiles().FirstOrDefault(p =>
            string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        return match ?? throw new FileNotFoundException($"Perfil no encontrado: {id}");
    }

    public static void SaveActiveId(string profileId)
    {
        var id = NormalizeId(profileId) ?? throw new ArgumentException("Id de perfil vacío.");
        _ = LoadById(id); // validate
        var path = Path.Combine(AppPaths.AppDirectory, ActiveFileName);
        AtomicFile.WriteAllText(path, id + Environment.NewLine);
    }

    public static LoadedGameProfile LoadFromDirectory(string directory)
    {
        var profilePath = Path.Combine(directory, "profile.json");
        var effectsPath = Path.Combine(directory, "effects.json");
        var giftsPath = Path.Combine(directory, "gifts.json");
        var json = File.ReadAllText(profilePath, Encoding.UTF8);
        var info = JsonSerializer.Deserialize<GameProfileInfo>(json, JsonDefaults.Options)
                   ?? throw new InvalidDataException($"profile.json inválido: {profilePath}");

        if (string.IsNullOrWhiteSpace(info.Id))
        {
            info.Id = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        if (string.IsNullOrWhiteSpace(info.DisplayName))
        {
            info.DisplayName = info.Id;
        }

        info.Labels = new Dictionary<string, string>(
            info.Labels ?? new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase);
        info.EnemyEffects ??= [];
        info.SingletonEffects ??= [];

        return new LoadedGameProfile
        {
            Info = info,
            Directory = directory,
            EffectsPath = effectsPath,
            GiftsPath = giftsPath,
            ProfileJsonPath = profilePath,
            IsCustom = IsUnderUserRoot(directory),
        };
    }

    private static string Normalize(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool IsUnderUserRoot(string directory)
    {
        try
        {
            var parent = Path.GetDirectoryName(Normalize(directory));
            return parent != null && string.Equals(Normalize(parent), Normalize(UserProfilesRoot), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string? FindProfilesRoot()
    {
        foreach (var root in AppPaths.CandidateRootsPublic())
        {
            var direct = Path.Combine(root, "profiles");
            if (Directory.Exists(direct) && Directory.GetDirectories(direct).Length > 0)
            {
                return direct;
            }

            var nested = Path.Combine(root, "config", "profiles");
            if (Directory.Exists(nested) && Directory.GetDirectories(nested).Length > 0)
            {
                return nested;
            }
        }

        return null;
    }

    private static string? ReadSavedActiveId()
    {
        foreach (var root in AppPaths.CandidateRootsPublic())
        {
            var path = Path.Combine(root, ActiveFileName);
            if (!File.Exists(path))
            {
                continue;
            }

            var text = File.ReadAllText(path, Encoding.UTF8).Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return NormalizeId(text);
            }
        }

        return null;
    }

    private static string? NormalizeId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant();
    }
}
