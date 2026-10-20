using HiveShock.Configuration;

namespace HiveShock.Tests.Configuration;

public class ShippedProfilesTests
{
    private static string ProfilesDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "config", "profiles");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("No se encontró config/profiles subiendo desde el directorio de pruebas.");
    }

    public static TheoryData<string> ProfileFolders()
    {
        var data = new TheoryData<string>();
        foreach (var dir in Directory.GetDirectories(ProfilesDirectory()).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            data.Add(Path.GetFileName(dir));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ProfileFolders))]
    public void Shipped_profile_loads_and_only_maps_effects_that_exist(string folder)
    {
        var profile = ProfileStore.LoadFromDirectory(Path.Combine(ProfilesDirectory(), folder));
        Assert.Equal(folder, profile.Id);
        Assert.Equal("json-line", profile.Info.Protocol);

        var effects = EffectCatalog.Load(profile.EffectsPath, profile.Info);
        Assert.NotEmpty(effects.Ids);

        var gifts = new GiftFileEditor(profile.GiftsPath);
        gifts.Load();

        var mapped = gifts.Gifts.Select(g => g.Effect)
            .Append(gifts.LikesEffect)
            .Append(gifts.FollowEffect)
            .Append(gifts.ShareEffect)
            .Where(e => !string.IsNullOrWhiteSpace(e));

        foreach (var effect in mapped)
        {
            Assert.True(effects.Contains(effect), $"{folder}: el regalo/evento apunta a «{effect}», que no existe en effects.json.");
        }

        // Los efectos resueltos deben llevar siempre "action": es lo único que entiende el juego.
        foreach (var id in effects.Ids)
        {
            var cmd = effects.Resolve(id, "tester");
            Assert.NotNull(cmd);
            Assert.True(cmd!.ContainsKey("action"), $"{folder}: «{id}» no define action.");
        }
    }

    [Fact]
    public void Banjo_kazooie_profile_listens_on_the_standard_ports_and_reports_deaths()
    {
        var profile = ProfileStore.LoadFromDirectory(Path.Combine(ProfilesDirectory(), "banjo-kazooie"));
        Assert.Equal(43000, profile.Info.GamePort);
        Assert.Equal(43002, profile.Info.EventPort);
        Assert.True(profile.Info.SupportsDeathEvents);

        var effects = EffectCatalog.Load(profile.EffectsPath, profile.Info);
        Assert.All(profile.Info.EnemyEffects, id => Assert.True(effects.Contains(id), $"enemyEffects lista «{id}», que no existe en effects.json."));
    }
}
