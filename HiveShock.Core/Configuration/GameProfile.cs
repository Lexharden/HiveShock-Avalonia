using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HiveShock.Configuration;

/// <summary>Producto / branding estable (independiente del juego activo).</summary>
public static class ProductInfo
{
    public const string Name = "HiveShock";
    public const string Tagline = "TikTok Live → efectos en el juego";
    public const string Vendor = "Yafel";
    public const string Website = "https://hiveshock.yafel.dev";

    public static string ExecutableFileName =>
        OperatingSystem.IsWindows() ? $"{Name}.exe" : Name;
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
}

/// <summary>Perfil resuelto con rutas absolutas a effects/gifts.</summary>
public sealed class LoadedGameProfile
{
    public required GameProfileInfo Info { get; init; }
    public required string Directory { get; init; }
    public required string EffectsPath { get; init; }
    public required string GiftsPath { get; init; }
    public required string ProfileJsonPath { get; init; }

    public string Id => Info.Id;
    public string DisplayName => Info.DisplayName;
}

public static class ProfileStore
{
    public const string DefaultProfileId = "majora-mask";
    private const string ActiveFileName = "active-profile.txt";

    public static string ProfilesRoot =>
        FindProfilesRoot()
        ?? throw new DirectoryNotFoundException(
            $"No se encontró la carpeta profiles/. Debe estar junto a {ProductInfo.ExecutableFileName} " +
            $"(carpeta: {AppPaths.AppDirectory}).");

    public static IReadOnlyList<LoadedGameProfile> ListProfiles()
    {
        var root = ProfilesRoot;
        var list = new List<LoadedGameProfile>();
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
                list.Add(LoadFromDirectory(dir));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[HiveShock] Perfil inválido en {dir}: {ex.Message}");
            }
        }

        if (list.Count == 0)
        {
            throw new InvalidOperationException(
                $"No hay perfiles válidos en {root}. Cada perfil necesita profile.json, effects.json y gifts.json.");
        }

        return list;
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
        File.WriteAllText(path, id + Environment.NewLine, new UTF8Encoding(false));
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
        };
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
