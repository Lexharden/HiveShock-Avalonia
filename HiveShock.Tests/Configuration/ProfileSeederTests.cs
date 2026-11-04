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
    public void Adds_new_effects_without_touching_the_ones_the_user_has_and_keeps_a_backup()
    {
        MakeProfile(Catalog, "banjo");
        MakeProfile(Profiles, "banjo", gifts: "MY-GIFTS");
        WriteEffects(Catalog, "banjo", """{"impulse":{"action":"impulse"},"new":{"action":"new"}}""");
        WriteEffects(Profiles, "banjo", """{"impulse":{"action":"impulse","speed":9},"mine":{"action":"x"}}""");
        var applied = new Dictionary<string, string>();

        var updated = ProfileSeeder.SyncEffects(Catalog, Profiles, applied);

        Assert.Equal(["banjo"], updated);
        var merged = System.Text.Json.Nodes.JsonNode.Parse(ReadEffects("banjo"))!;
        Assert.Equal(9, (int)merged["impulse"]!["speed"]!);
        Assert.NotNull(merged["mine"]);
        Assert.Equal("new", (string)merged["new"]!["action"]!);
        Assert.Contains("\"mine\"", File.ReadAllText(Path.Combine(Profiles, "banjo", ProfileSeeder.EffectsBackupFile)));
        Assert.Equal("MY-GIFTS", File.ReadAllText(Path.Combine(Profiles, "banjo", "gifts.json")));
    }

    [Fact]
    public void Never_removes_or_changes_user_effects_even_when_the_catalog_drops_one()
    {
        MakeProfile(Catalog, "oot");
        MakeProfile(Profiles, "oot");
        WriteEffects(Catalog, "oot", """{"a":{"action":"a"}}""");
        const string mine = """{"a":{"action":"custom"},"ganon":{"action":"ganon"}}""";
        WriteEffects(Profiles, "oot", mine);

        Assert.Empty(ProfileSeeder.SyncEffects(Catalog, Profiles, new Dictionary<string, string>()));
        Assert.Equal(mine, ReadEffects("oot"));
    }

    [Fact]
    public void Effects_merge_runs_once_per_catalog_version()
    {
        MakeProfile(Catalog, "banjo");
        MakeProfile(Profiles, "banjo");
        WriteEffects(Catalog, "banjo", """{"v1":{}}""");
        var applied = new Dictionary<string, string>();
        ProfileSeeder.SyncEffects(Catalog, Profiles, applied);

        // El usuario borra el efecto a propósito: no vuelve hasta que el catálogo cambie.
        WriteEffects(Profiles, "banjo", "{}");
        Assert.Empty(ProfileSeeder.SyncEffects(Catalog, Profiles, applied));
        Assert.Equal("{}", ReadEffects("banjo"));

        WriteEffects(Catalog, "banjo", """{"v1":{},"v2":{}}""");
        Assert.Equal(["banjo"], ProfileSeeder.SyncEffects(Catalog, Profiles, applied));
        var merged = System.Text.Json.Nodes.JsonNode.Parse(ReadEffects("banjo"))!.AsObject();
        Assert.Equal(["v1", "v2"], merged.Select(p => p.Key).ToArray());
    }

    [Fact]
    public void Effects_merge_skips_an_unreadable_effects_file()
    {
        MakeProfile(Catalog, "banjo");
        MakeProfile(Profiles, "banjo");
        WriteEffects(Catalog, "banjo", """{"a":{}}""");
        WriteEffects(Profiles, "banjo", "{ roto");

        Assert.Empty(ProfileSeeder.SyncEffects(Catalog, Profiles, new Dictionary<string, string>()));
        Assert.Equal("{ roto", ReadEffects("banjo"));
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

    private string ReadProfile(string name) => File.ReadAllText(Path.Combine(Profiles, name, "profile.json"));

    [Fact]
    public void Updates_a_label_the_user_never_changed_but_keeps_the_ones_they_edited()
    {
        MakeProfile(Catalog, "oot");
        MakeProfile(Profiles, "oot");
        var applied = new Dictionary<string, string>();

        // v1: se siembra "Hada" y "Navi".
        WriteProfile(Catalog, "oot", """{"labels":{"fairy":"Hada","navi":"Navi"}}""");
        WriteProfile(Profiles, "oot", """{"labels":{"fairy":"Hada","navi":"Navi"}}""");
        ProfileSeeder.SyncProfileMeta(Catalog, Profiles, applied);

        // El usuario cambia "navi"; v2 cambia los dos nombres.
        WriteProfile(Profiles, "oot", """{"labels":{"fairy":"Hada","navi":"Mi Navi"}}""");
        WriteProfile(Catalog, "oot", """{"labels":{"fairy":"Hada (cura)","navi":"Navi (guía)"}}""");
        Assert.Equal(["oot"], ProfileSeeder.SyncProfileMeta(Catalog, Profiles, applied));

        var labels = System.Text.Json.Nodes.JsonNode.Parse(ReadProfile("oot"))!["labels"]!;
        Assert.Equal("Hada (cura)", (string)labels["fairy"]!);
        Assert.Equal("Mi Navi", (string)labels["navi"]!);
    }

    [Fact]
    public void Without_a_baseline_an_existing_label_that_differs_is_treated_as_the_users()
    {
        MakeProfile(Catalog, "oot");
        MakeProfile(Profiles, "oot");
        WriteProfile(Catalog, "oot", """{"labels":{"fairy":"Hada (cura)","owl":"Búho"}}""");
        WriteProfile(Profiles, "oot", """{"labels":{"fairy":"Hada"}}""");

        ProfileSeeder.SyncProfileMeta(Catalog, Profiles, new Dictionary<string, string>());

        var labels = System.Text.Json.Nodes.JsonNode.Parse(ReadProfile("oot"))!["labels"]!;
        Assert.Equal("Hada", (string)labels["fairy"]!);
        Assert.Equal("Búho", (string)labels["owl"]!);
    }

    [Fact]
    public void Updates_an_effect_definition_the_user_never_changed()
    {
        MakeProfile(Catalog, "oot");
        MakeProfile(Profiles, "oot");
        var applied = new Dictionary<string, string>();
        WriteEffects(Catalog, "oot", """{"a":{"action":"a","n":1},"b":{"action":"b"}}""");
        WriteEffects(Profiles, "oot", """{"a":{"action":"a","n":1},"b":{"action":"b"}}""");
        ProfileSeeder.SyncEffects(Catalog, Profiles, applied);

        WriteEffects(Profiles, "oot", """{"a":{"action":"a","n":1},"b":{"action":"mio"}}""");
        WriteEffects(Catalog, "oot", """{"a":{"action":"a","n":2},"b":{"action":"b2"}}""");
        Assert.Equal(["oot"], ProfileSeeder.SyncEffects(Catalog, Profiles, applied));

        var effects = System.Text.Json.Nodes.JsonNode.Parse(ReadEffects("oot"))!;
        Assert.Equal(2, (int)effects["a"]!["n"]!);
        Assert.Equal("mio", (string)effects["b"]!["action"]!);
    }

    [Fact]
    public void Merges_new_singleton_effects()
    {
        MakeProfile(Catalog, "oot");
        MakeProfile(Profiles, "oot");
        WriteProfile(Catalog, "oot", """{"singletonEffects":["delete_save","clear_enemies"]}""");
        WriteProfile(Profiles, "oot", """{"singletonEffects":["delete_save"]}""");

        ProfileSeeder.SyncProfileMeta(Catalog, Profiles, new Dictionary<string, string>());

        var singles = System.Text.Json.Nodes.JsonNode.Parse(ReadProfile("oot"))!["singletonEffects"]!.AsArray();
        Assert.Equal(["delete_save", "clear_enemies"], singles.Select(n => (string)n!).ToArray());
    }

    [Fact]
    public void Unexpected_value_types_in_the_users_profile_do_not_break_the_sync()
    {
        MakeProfile(Catalog, "bad");
        MakeProfile(Profiles, "bad");
        MakeProfile(Catalog, "good");
        MakeProfile(Profiles, "good");
        WriteProfile(Catalog, "bad", """{"enemyEffects":["a"],"labels":{"a":"A"}}""");
        WriteProfile(Profiles, "bad", """{"enemyEffects":[1,null,{"x":1}],"labels":[1]}""");
        WriteProfile(Catalog, "good", """{"labels":{"a":"A"}}""");
        WriteProfile(Profiles, "good", """{"labels":{}}""");

        var updated = ProfileSeeder.SyncProfileMeta(Catalog, Profiles, new Dictionary<string, string>());

        Assert.Contains("good", updated);
        var bad = System.Text.Json.Nodes.JsonNode.Parse(ReadProfile("bad"))!;
        Assert.True(bad["labels"] is System.Text.Json.Nodes.JsonArray);
        Assert.Equal(4, bad["enemyEffects"]!.AsArray().Count);
    }

    [Theory]
    [InlineData("Hada", "Hada (cura)")]
    [InlineData("Mi hada", "Mi hada")]
    public void The_old_ocarina_fairy_name_is_updated_only_if_the_user_kept_it(string installed, string expected)
    {
        MakeProfile(Catalog, "ocarina-of-time");
        MakeProfile(Profiles, "ocarina-of-time");
        WriteProfile(Catalog, "ocarina-of-time", """{"labels":{"fairy":"Hada (cura)"}}""");
        WriteProfile(Profiles, "ocarina-of-time", "{\"labels\":{\"fairy\":\"" + installed + "\"}}");

        ProfileSeeder.SyncProfileMeta(Catalog, Profiles, new Dictionary<string, string>());

        var labels = System.Text.Json.Nodes.JsonNode.Parse(ReadProfile("ocarina-of-time"))!["labels"]!;
        Assert.Equal(expected, (string)labels["fairy"]!);
    }
}
