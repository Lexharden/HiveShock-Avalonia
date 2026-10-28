using HiveShock.Configuration;
using HiveShock.Updates;

namespace HiveShock.Tests.Configuration;

public sealed class ProfileSeederTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hiveshock-seed-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Catalog => Path.Combine(_root, "profiles-catalog");

    private string Profiles => Path.Combine(_root, "profiles");

    private void MakeProfile(string baseDir, string name, string gifts = "CATALOG-GIFTS", bool complete = true)
    {
        var dir = Path.Combine(baseDir, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "profile.json"), "{}");
        File.WriteAllText(Path.Combine(dir, "gifts.json"), gifts);
        if (complete)
        {
            File.WriteAllText(Path.Combine(dir, "effects.json"), "{}");
        }
    }

    private void WriteEffects(string baseDir, string name, string text) =>
        File.WriteAllText(Path.Combine(baseDir, name, "effects.json"), text);

    private string ReadEffects(string name) => File.ReadAllText(Path.Combine(Profiles, name, "effects.json"));

    [Fact]
    public void Replaces_effects_when_the_catalog_has_a_new_version_and_keeps_a_backup()
    {
        MakeProfile(Catalog, "banjo");
        MakeProfile(Profiles, "banjo", gifts: "MY-GIFTS");
        WriteEffects(Catalog, "banjo", "{\"new\":{}}");
        WriteEffects(Profiles, "banjo", "{\"old\":{}}");
        var applied = new Dictionary<string, string>();

        var updated = ProfileSeeder.SyncEffects(Catalog, Profiles, applied);

        Assert.Equal(["banjo"], updated);
        Assert.Equal("{\"new\":{}}", ReadEffects("banjo"));
        Assert.Equal("{\"old\":{}}", File.ReadAllText(Path.Combine(Profiles, "banjo", ProfileSeeder.EffectsBackupFile)));
        Assert.Equal("MY-GIFTS", File.ReadAllText(Path.Combine(Profiles, "banjo", "gifts.json")));
    }

    [Fact]
    public void Keeps_user_edits_until_the_catalog_changes_again()
    {
        MakeProfile(Catalog, "banjo");
        MakeProfile(Profiles, "banjo");
        WriteEffects(Catalog, "banjo", "{\"v1\":{}}");
        var applied = new Dictionary<string, string>();
        ProfileSeeder.SyncEffects(Catalog, Profiles, applied);

        WriteEffects(Profiles, "banjo", "{\"mine\":{}}");
        Assert.Empty(ProfileSeeder.SyncEffects(Catalog, Profiles, applied));
        Assert.Equal("{\"mine\":{}}", ReadEffects("banjo"));

        WriteEffects(Catalog, "banjo", "{\"v2\":{}}");
        Assert.Equal(["banjo"], ProfileSeeder.SyncEffects(Catalog, Profiles, applied));
        Assert.Equal("{\"v2\":{}}", ReadEffects("banjo"));
    }

    [Fact]
    public void Does_not_touch_deleted_or_custom_profiles()
    {
        MakeProfile(Catalog, "banjo");
        MakeProfile(Profiles, "mi-perfil");
        WriteEffects(Profiles, "mi-perfil", "{\"mine\":{}}");

        var updated = ProfileSeeder.SyncEffects(Catalog, Profiles, new Dictionary<string, string>());

        Assert.Empty(updated);
        Assert.False(Directory.Exists(Path.Combine(Profiles, "banjo")));
        Assert.Equal("{\"mine\":{}}", ReadEffects("mi-perfil"));
    }

    private void WriteProfile(string baseDir, string name, string json) =>
        File.WriteAllText(Path.Combine(baseDir, name, "profile.json"), json);

    [Fact]
    public void Merges_new_labels_and_enemy_effects_without_touching_what_the_user_has()
    {
        MakeProfile(Catalog, "banjo");
        MakeProfile(Profiles, "banjo");
        WriteProfile(Catalog, "banjo",
            """{"gamePort":43000,"enemyEffects":["a","b"],"labels":{"a":"Catálogo A","b":"Catálogo B"}}""");
        WriteProfile(Profiles, "banjo",
            """{"gamePort":9999,"enemyEffects":["a"],"labels":{"a":"Mi nombre"}}""");

        var updated = ProfileSeeder.SyncProfileMeta(Catalog, Profiles, new Dictionary<string, string>());

        Assert.Equal(["banjo"], updated);
        var merged = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(Profiles, "banjo", "profile.json")))!;
        Assert.Equal(9999, (int)merged["gamePort"]!);
        Assert.Equal("Mi nombre", (string)merged["labels"]!["a"]!);
        Assert.Equal("Catálogo B", (string)merged["labels"]!["b"]!);
        Assert.Equal(["a", "b"], merged["enemyEffects"]!.AsArray().Select(n => (string)n!).ToArray());
        Assert.True(File.Exists(Path.Combine(Profiles, "banjo", ProfileSeeder.ProfileBackupFile)));
    }

    [Fact]
    public void Profile_merge_runs_once_per_catalog_version()
    {
        MakeProfile(Catalog, "banjo");
        MakeProfile(Profiles, "banjo");
        WriteProfile(Catalog, "banjo", """{"enemyEffects":["a"],"labels":{"a":"A"}}""");
        WriteProfile(Profiles, "banjo", """{"enemyEffects":[],"labels":{}}""");
        var applied = new Dictionary<string, string>();
        ProfileSeeder.SyncProfileMeta(Catalog, Profiles, applied);

        // El usuario quita el enemigo a propósito: no vuelve hasta que el catálogo cambie.
        WriteProfile(Profiles, "banjo", """{"enemyEffects":[],"labels":{"a":"A"}}""");

        Assert.Empty(ProfileSeeder.SyncProfileMeta(Catalog, Profiles, applied));
        Assert.DoesNotContain("\"a\"", File.ReadAllText(Path.Combine(Profiles, "banjo", "profile.json")).Split("enemyEffects")[1].Split("labels")[0]);
    }

    [Fact]
    public void Profile_merge_skips_an_unreadable_profile()
    {
        MakeProfile(Catalog, "banjo");
        MakeProfile(Profiles, "banjo");
        WriteProfile(Catalog, "banjo", """{"labels":{"a":"A"}}""");
        WriteProfile(Profiles, "banjo", "{ not json");

        Assert.Empty(ProfileSeeder.SyncProfileMeta(Catalog, Profiles, new Dictionary<string, string>()));
        Assert.Equal("{ not json", File.ReadAllText(Path.Combine(Profiles, "banjo", "profile.json")));
    }

    [Fact]
    public void Installs_the_profiles_the_user_does_not_have()
    {
        MakeProfile(Catalog, "mario");
        MakeProfile(Catalog, "banjo");
        var seeded = new HashSet<string>();

        var installed = ProfileSeeder.Seed(Catalog, Profiles, seeded);

        Assert.Equal(["banjo", "mario"], installed);
        Assert.True(File.Exists(Path.Combine(Profiles, "mario", "effects.json")));
        Assert.Contains("mario", seeded);
    }

    [Fact]
    public void Never_overwrites_a_profile_the_user_already_has()
    {
        MakeProfile(Catalog, "oot", gifts: "CATALOG-GIFTS");
        MakeProfile(Profiles, "oot", gifts: "MY-EDITED-GIFTS");
        var seeded = new HashSet<string>();

        var installed = ProfileSeeder.Seed(Catalog, Profiles, seeded);

        Assert.Empty(installed);
        Assert.Equal("MY-EDITED-GIFTS", File.ReadAllText(Path.Combine(Profiles, "oot", "gifts.json")));
        Assert.Contains("oot", seeded);
    }

    [Fact]
    public void A_profile_the_user_deleted_on_purpose_does_not_come_back()
    {
        MakeProfile(Catalog, "oot");
        var seeded = new HashSet<string> { "oot" }; // ya se había visto/instalado antes

        var installed = ProfileSeeder.Seed(Catalog, Profiles, seeded);

        Assert.Empty(installed);
        Assert.False(Directory.Exists(Path.Combine(Profiles, "oot")));
    }

    [Fact]
    public void An_incomplete_profile_is_not_installed()
    {
        MakeProfile(Catalog, "roto", complete: false);

        Assert.Empty(ProfileSeeder.Seed(Catalog, Profiles, new HashSet<string>()));
        Assert.False(Directory.Exists(Path.Combine(Profiles, "roto")));
    }

    [Fact]
    public void Running_it_again_changes_nothing()
    {
        MakeProfile(Catalog, "mario");
        var seeded = new HashSet<string>();
        ProfileSeeder.Seed(Catalog, Profiles, seeded);
        File.WriteAllText(Path.Combine(Profiles, "mario", "gifts.json"), "EDITADO");

        Assert.Empty(ProfileSeeder.Seed(Catalog, Profiles, seeded));
        Assert.Equal("EDITADO", File.ReadAllText(Path.Combine(Profiles, "mario", "gifts.json")));
    }

    [Fact]
    public void Missing_catalog_folder_is_fine()
    {
        Assert.Empty(ProfileSeeder.Seed(Path.Combine(_root, "no-existe"), Profiles, new HashSet<string>()));
    }

    [Theory]
    [InlineData("profiles-catalog/mario/profile.json", false)]
    [InlineData("profiles/mario/profile.json", true)]
    public void The_catalog_folder_travels_even_with_an_old_updater_that_protects_profiles(string rel, bool protectedPath) =>
        Assert.Equal(protectedPath, new UpdateLayout("t", "s", "", "HiveShock.exe").IsProtected(rel));

    [Fact]
    public void The_catalog_folder_also_travels_inside_a_mac_bundle()
    {
        var layout = new UpdateLayout("t", "s", "Contents/MacOS/", "Contents/MacOS/HiveShock");
        Assert.False(layout.IsProtected("Contents/MacOS/profiles-catalog/mario/profile.json"));
    }
}
