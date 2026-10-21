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
