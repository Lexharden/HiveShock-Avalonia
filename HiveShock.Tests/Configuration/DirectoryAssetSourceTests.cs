using HiveShock.Configuration;

namespace HiveShock.Tests.Configuration;

public sealed class DirectoryAssetSourceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hiveshock-assets-" + Guid.NewGuid().ToString("N"));

    public DirectoryAssetSourceTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "bundle", "profiles-catalog", "mario"));
        File.WriteAllText(Path.Combine(_root, "bundle", "profiles-catalog", "mario", "profile.json"), "{}");
        File.WriteAllText(Path.Combine(_root, "bundle", "tiktok_gifts.json"), "[]");
        File.WriteAllText(Path.Combine(_root, "secreto.txt"), "no");
    }

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

    private DirectoryAssetSource Source => new(Path.Combine(_root, "bundle"));

    [Fact]
    public void Lists_children_and_opens_files()
    {
        Assert.Equal(["mario"], Source.List("profiles-catalog"));
        Assert.Empty(Source.List("profiles-catalog/mario/profile.json"));
        Assert.Empty(Source.List("no-existe"));
        using var stream = Source.Open("tiktok_gifts.json");
        Assert.NotNull(stream);
        Assert.Null(Source.Open("no-existe.json"));
    }

    [Fact]
    public void Does_not_leave_the_root_folder()
    {
        Assert.Null(Source.Open("../secreto.txt"));
        Assert.Empty(Source.List(".."));
    }

    [Fact]
    public void Works_with_the_asset_extractor()
    {
        var dest = Path.Combine(_root, "dest");
        var copied = AssetExtractor.CopyTree(Source, "profiles-catalog", Path.Combine(dest, "profiles-catalog"), overwrite: true);

        Assert.Equal(1, copied);
        Assert.True(File.Exists(Path.Combine(dest, "profiles-catalog", "mario", "profile.json")));
    }
}
