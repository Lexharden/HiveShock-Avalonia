using HiveShock.Configuration;

namespace HiveShock.Tests.Configuration;

public sealed class AssetExtractorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hiveshock-assets-" + Guid.NewGuid().ToString("N"));

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

    private sealed class FakeAssets(Dictionary<string, string> files) : IAssetSource
    {
        public IReadOnlyList<string> List(string path)
        {
            var prefix = path.TrimEnd('/') + "/";
            return files.Keys
                .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
                .Select(k => k[prefix.Length..].Split('/')[0])
                .Distinct()
                .ToList();
        }

        public Stream? Open(string path) =>
            files.TryGetValue(path, out var text) ? new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text)) : null;
    }

    [Fact]
    public void CopyTree_copies_nested_files()
    {
        var src = new FakeAssets(new()
        {
            ["cat/mario/profile.json"] = "p",
            ["cat/mario/sub/x.json"] = "x",
            ["cat/zelda/profile.json"] = "z",
        });

        var count = AssetExtractor.CopyTree(src, "cat", _root, overwrite: true);

        Assert.Equal(3, count);
        Assert.Equal("x", File.ReadAllText(Path.Combine(_root, "mario", "sub", "x.json")));
    }

    [Fact]
    public void CopyFile_without_overwrite_keeps_the_users_file()
    {
        var dest = Path.Combine(_root, "gift-catalog.json");
        Directory.CreateDirectory(_root);
        File.WriteAllText(dest, "USER");
        var src = new FakeAssets(new() { ["gift-catalog.json"] = "APP" });

        Assert.False(AssetExtractor.CopyFile(src, "gift-catalog.json", dest, overwrite: false));
        Assert.Equal("USER", File.ReadAllText(dest));
    }

    [Fact]
    public void CopyFile_with_overwrite_renews_the_file_and_leaves_no_temp()
    {
        var dest = Path.Combine(_root, "tiktok_gifts.json");
        Directory.CreateDirectory(_root);
        File.WriteAllText(dest, "OLD");
        var src = new FakeAssets(new() { ["tiktok_gifts.json"] = "NEW" });

        Assert.True(AssetExtractor.CopyFile(src, "tiktok_gifts.json", dest, overwrite: true));
        Assert.Equal("NEW", File.ReadAllText(dest));
        Assert.False(File.Exists(dest + ".tmp"));
    }

    [Fact]
    public void CopyFile_missing_asset_returns_false_without_throwing()
    {
        var src = new FakeAssets(new());

        Assert.False(AssetExtractor.CopyFile(src, "nope.json", Path.Combine(_root, "nope.json"), overwrite: true));
        Assert.False(File.Exists(Path.Combine(_root, "nope.json")));
    }
}
