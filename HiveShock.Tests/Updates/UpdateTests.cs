using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using HiveShock.Updates;

namespace HiveShock.Tests.Updates;

public sealed class SemVerTests
{
    [Theory]
    [InlineData("2.2.0", "2.1.9")]
    [InlineData("2.2.0", "2.2.0-rc.1")]
    [InlineData("2.2.0-rc.1", "2.2.0-beta.3")]
    [InlineData("2.2.0-beta.1", "2.2.0-alpha.9")]
    [InlineData("2.2.0-alpha.10", "2.2.0-alpha.2")]
    [InlineData("2.2.0-zeldathon.3", "2.2.0-zeldathon.2")]
    [InlineData("v3.0.0", "2.99.99")]
    public void Orders_versions(string higher, string lower) =>
        Assert.True(SemVer.TryParse(higher)! > SemVer.TryParse(lower)!);

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("1.2")]
    [InlineData("1.2.x")]
    [InlineData("1.2.3-")]
    public void Rejects_garbage(string text) => Assert.Null(SemVer.TryParse(text));

    [Fact]
    public void Ignores_build_metadata_and_prefix() =>
        Assert.Equal(SemVer.TryParse("2.2.0"), SemVer.TryParse("v2.2.0+abc123"));
}

internal sealed class FakeHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
    public List<HttpRequestMessage> Requests { get; } = [];

    public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        return Task.FromResult(_respond(request));
    }
}

public sealed class GitHubReleaseClientTests
{
    private const string Json = """
    [
      {"tag_name":"v2.3.0","name":"HiveShock 2.3.0","draft":false,"prerelease":false,"published_at":"2026-10-20T10:00:00Z",
       "body":"Notas","html_url":"https://x/r/2.3.0",
       "assets":[{"name":"HiveShock-2.3.0-win-x64-update.zip","browser_download_url":"https://x/a.zip","size":10,"digest":"sha256:ABCDEF"}]},
      {"tag_name":"v2.4.0-beta.1","draft":false,"prerelease":true,"assets":[]},
      {"tag_name":"v9.9.9","draft":true,"assets":[]},
      {"tag_name":"nightly","draft":false,"assets":[]}
    ]
    """;

    private static HttpResponseMessage Ok(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public void Parse_skips_drafts_and_non_versions_and_sorts_newest_first()
    {
        var list = GitHubReleaseClient.Parse(Json);
        Assert.Equal(["2.4.0-beta.1", "2.3.0"], list.Select(r => r.Version));
        Assert.True(list[0].Prerelease);
        var asset = Assert.Single(list[1].Assets);
        Assert.Equal("abcdef", asset.Sha256);
    }

    [Fact]
    public async Task Fetch_sends_etag_and_handles_304()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotModified));
        var client = new GitHubReleaseClient(new HttpClient(handler));
        var result = await client.FetchAsync("\"abc\"", CancellationToken.None);
        Assert.True(result.NotModified);
        Assert.Equal("\"abc\"", handler.Requests[0].Headers.IfNoneMatch.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Fetch_reports_errors(HttpStatusCode code)
    {
        var client = new GitHubReleaseClient(new HttpClient(new FakeHandler(_ => new HttpResponseMessage(code))));
        await Assert.ThrowsAsync<UpdateException>(() => client.FetchAsync(null, CancellationToken.None));
    }

    [Fact]
    public async Task Fetch_rejects_broken_json()
    {
        var client = new GitHubReleaseClient(new HttpClient(new FakeHandler(_ => Ok("{ not json"))));
        await Assert.ThrowsAsync<UpdateException>(() => client.FetchAsync(null, CancellationToken.None));
    }

    [Fact]
    public void Selects_asset_for_platform()
    {
        var release = GitHubReleaseClient.Parse(Json).Last();
        Assert.NotNull(UpdatePlatform.SelectAsset(release, "win-x64"));
        Assert.Null(UpdatePlatform.SelectAsset(release, "linux-arm64"));
    }

    [Fact]
    public void Finds_hash_in_sums_file()
    {
        var hash = new string('a', 64);
        Assert.Equal(hash, GitHubReleaseClient.FindHash($"{hash}  HiveShock-1-win-x64-update.zip\n", "HiveShock-1-win-x64-update.zip"));
        Assert.Null(GitHubReleaseClient.FindHash($"{hash}  other.zip\n", "HiveShock-1-win-x64-update.zip"));
    }
}

public abstract class TempDirTest : IDisposable
{
    protected string Dir { get; } = Path.Combine(Path.GetTempPath(), "hs-upd-" + Guid.NewGuid().ToString("N"));

    protected TempDirTest() => Directory.CreateDirectory(Dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Dir, true);
        }
        catch
        {
            // ignore
        }
    }

    protected string Make(string rel, string content = "x")
    {
        var path = Path.Combine(Dir, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    protected string MakeZip(string name, params (string Path, string Content)[] files)
    {
        var zipPath = Path.Combine(Dir, name);
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var (p, c) in files)
        {
            var e = zip.CreateEntry(p);
            using var w = new StreamWriter(e.Open());
            w.Write(c);
        }

        return zipPath;
    }
}

public sealed class UpdateDownloaderTests : TempDirTest
{
    private static string Sha(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    [Fact]
    public async Task Downloads_and_verifies()
    {
        var zip = MakeZip("src.zip", ("HiveShock.exe", "exe"));
        var bytes = File.ReadAllBytes(zip);
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        var asset = new ReleaseAsset("a.zip", "https://x/a.zip", bytes.Length, Sha(zip));
        var release = new ReleaseInfo("v1.0.0", "1.0.0", "t", null, false, "", "", [asset]);

        var result = await new UpdateDownloader(new HttpClient(handler))
            .DownloadAsync(release, asset, Path.Combine(Dir, "dl"), null, CancellationToken.None);

        Assert.True(result.Verified);
        Assert.True(File.Exists(result.ZipPath));
    }

    [Fact]
    public async Task Rejects_wrong_hash_and_deletes_file()
    {
        var zip = MakeZip("src.zip", ("HiveShock.exe", "exe"));
        var bytes = File.ReadAllBytes(zip);
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        var asset = new ReleaseAsset("a.zip", "https://x/a.zip", bytes.Length, new string('0', 64));
        var release = new ReleaseInfo("v1.0.0", "1.0.0", "t", null, false, "", "", [asset]);

        await Assert.ThrowsAsync<UpdateException>(() => new UpdateDownloader(new HttpClient(handler))
            .DownloadAsync(release, asset, Path.Combine(Dir, "dl"), null, CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(Dir, "dl", "a.zip")));
    }

    [Fact]
    public async Task Resumes_partial_download()
    {
        var zip = MakeZip("src.zip", ("HiveShock.exe", new string('z', 4000)));
        var bytes = File.ReadAllBytes(zip);
        var dl = Path.Combine(Dir, "dl");
        Directory.CreateDirectory(dl);
        await File.WriteAllBytesAsync(Path.Combine(dl, "a.zip.part"), bytes[..100]);
        var handler = new FakeHandler(req =>
        {
            Assert.Equal(100, req.Headers.Range!.Ranges.First().From);
            return new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(bytes[100..]) };
        });
        var asset = new ReleaseAsset("a.zip", "https://x/a.zip", bytes.Length, Sha(zip));
        var release = new ReleaseInfo("v1.0.0", "1.0.0", "t", null, false, "", "", [asset]);

        var result = await new UpdateDownloader(new HttpClient(handler))
            .DownloadAsync(release, asset, dl, null, CancellationToken.None);

        Assert.Equal(bytes, await File.ReadAllBytesAsync(result.ZipPath));
    }

    [Fact]
    public void Validate_detects_corrupt_zip()
    {
        var path = Make("bad.zip", "no soy un zip");
        var asset = new ReleaseAsset("bad.zip", "u", 0, null);
        Assert.NotNull(UpdateDownloader.Validate(path, asset, null));
    }
}

public sealed class UpdateInstallerTests : TempDirTest
{
    [Fact]
    public void Stage_rejects_zip_slip()
    {
        var zip = MakeZip("evil.zip", ("../evil.txt", "x"), ("HiveShock.exe", "x"));
        var paths = new UpdatePaths(Path.Combine(Dir, "upd"));
        Assert.Throws<UpdateException>(() => UpdateInstaller.Stage(zip, paths, Path.Combine(Dir, "app")));
        Assert.False(File.Exists(Path.Combine(Dir, "evil.txt")));
    }

    [Fact]
    public void Stage_requires_executable()
    {
        var zip = MakeZip("noexe.zip", ("readme.txt", "x"));
        var paths = new UpdatePaths(Path.Combine(Dir, "upd"));
        Directory.CreateDirectory(Path.Combine(Dir, "app"));
        Assert.Throws<UpdateException>(() => UpdateInstaller.Stage(zip, paths, Path.Combine(Dir, "app")));
    }

    [Fact]
    public void Stage_rejects_corrupt_windows_executable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var zip = MakeZip("bad.zip", ("HiveShock.exe", "no soy un ejecutable"));
        var paths = new UpdatePaths(Path.Combine(Dir, "upd"));
        Directory.CreateDirectory(Path.Combine(Dir, "app"));
        Assert.Throws<UpdateException>(() => UpdateInstaller.Stage(zip, paths, Path.Combine(Dir, "app")));
    }

    [Theory]
    [InlineData("profiles/ocarina/gifts.json", true)]
    [InlineData(".env", true)]
    [InlineData("gift-catalog.json", true)]
    [InlineData("active-profile.txt", true)]
    [InlineData("logs/a.log", true)]
    [InlineData(".hiveshock-ui.json", true)]
    [InlineData("tiktok_gifts.json", false)]
    [InlineData("gifts-images/1.webp", false)]
    [InlineData("HiveShock.exe", false)]
    public void Protects_user_data(string rel, bool expected) =>
        Assert.Equal(expected, new UpdateLayout("t", "s", "", "HiveShock.exe").IsProtected(rel));

    [Fact]
    public void Protects_user_data_inside_mac_bundle()
    {
        var layout = new UpdateLayout("t", "s", "Contents/MacOS/", "Contents/MacOS/HiveShock");
        Assert.True(layout.IsProtected("Contents/MacOS/profiles/a/b.json"));
        Assert.True(layout.IsProtected("Contents/MacOS/.env"));
        Assert.False(layout.IsProtected("Contents/MacOS/HiveShock"));
        Assert.False(layout.IsProtected("Contents/Info.plist"));
    }
}

public sealed class UpdateApplierTests : TempDirTest
{
    private sealed class FakeHost : IApplierHost
    {
        public bool ExitOk = true;
        public bool ConfirmStart = true;
        public string? OkMarker;
        public List<string> Launched { get; } = [];
        public List<int> Killed { get; } = [];
        public bool WaitForExit(int pid, TimeSpan timeout) => ExitOk;
        public int Launch(string executable)
        {
            Launched.Add(executable);
            if (ConfirmStart && OkMarker is not null && Launched.Count == 1)
            {
                File.WriteAllText(OkMarker, "ok");
            }

            return 4242;
        }

        public void Kill(int pid) => Killed.Add(pid);
        public bool WaitForFile(string path, TimeSpan timeout) => File.Exists(path);
        public void Sleep(TimeSpan time) { }
    }

    private (ApplyPlan Plan, string App) Arrange()
    {
        var app = Path.Combine(Dir, "app");
        Make("app/HiveShock.exe", "OLD");
        Make("app/tiktok_gifts.json", "OLD-GIFTS");
        Make("app/.env", "SECRET");
        Make("app/gift-catalog.json", "MY-CATALOG");
        Make("app/profiles/oot/gifts.json", "MY-GIFTS");
        Make("src/HiveShock.exe", "NEW");
        Make("src/tiktok_gifts.json", "NEW-GIFTS");
        Make("src/gifts-images/1.webp", "IMG");
        Make("src/gift-catalog.json", "DEFAULT-CATALOG");
        Make("src/profiles/oot/gifts.json", "DEFAULT-GIFTS");
        var plan = new ApplyPlan
        {
            Pid = 1,
            TargetRoot = app,
            SourceRoot = Path.Combine(Dir, "src"),
            BackupDir = Path.Combine(Dir, "backup"),
            StagingDir = Path.Combine(Dir, "src"),
            Executable = Path.Combine(app, "HiveShock.exe"),
            FromVersion = "2.2.0",
            ToVersion = "2.3.0",
            ResultFile = Path.Combine(Dir, "result.json"),
            OkMarker = Path.Combine(Dir, "ok"),
        };
        return (plan, app);
    }

    [Fact]
    public void Applies_without_touching_user_data()
    {
        var (plan, app) = Arrange();
        var host = new FakeHost { OkMarker = plan.OkMarker };

        var result = UpdateApplier.Apply(plan, host);

        Assert.True(result.Ok, result.Error);
        Assert.Equal("NEW", File.ReadAllText(Path.Combine(app, "HiveShock.exe")));
        Assert.Equal("NEW-GIFTS", File.ReadAllText(Path.Combine(app, "tiktok_gifts.json")));
        Assert.True(File.Exists(Path.Combine(app, "gifts-images", "1.webp")));
        Assert.Equal("SECRET", File.ReadAllText(Path.Combine(app, ".env")));
        Assert.Equal("MY-CATALOG", File.ReadAllText(Path.Combine(app, "gift-catalog.json")));
        Assert.Equal("MY-GIFTS", File.ReadAllText(Path.Combine(app, "profiles", "oot", "gifts.json")));
        Assert.Empty(Directory.GetFiles(app, "*.hsnew", SearchOption.AllDirectories));
    }

    [Fact]
    public void Rolls_back_when_new_version_never_confirms()
    {
        var (plan, app) = Arrange();
        var host = new FakeHost { OkMarker = plan.OkMarker, ConfirmStart = false };

        var result = UpdateApplier.Apply(plan, host);

        Assert.False(result.Ok);
        Assert.True(result.RolledBack);
        Assert.Equal("OLD", File.ReadAllText(Path.Combine(app, "HiveShock.exe")));
        Assert.Equal("OLD-GIFTS", File.ReadAllText(Path.Combine(app, "tiktok_gifts.json")));
        Assert.False(File.Exists(Path.Combine(app, "gifts-images", "1.webp")), "los archivos nuevos se quitan");
        Assert.Equal("SECRET", File.ReadAllText(Path.Combine(app, ".env")));
        Assert.Equal([4242], host.Killed);
        Assert.Equal(2, host.Launched.Count); // nueva (fallida) + anterior restaurada
    }

    [Fact]
    public void Does_nothing_if_app_does_not_close()
    {
        var (plan, app) = Arrange();
        var result = UpdateApplier.Apply(plan, new FakeHost { ExitOk = false });

        Assert.False(result.Ok);
        Assert.Equal("OLD", File.ReadAllText(Path.Combine(app, "HiveShock.exe")));
    }

    [Fact]
    public void Startup_confirms_only_the_pending_version()
    {
        var paths = new UpdatePaths(Path.Combine(Dir, "upd"));
        Directory.CreateDirectory(paths.Root);
        File.WriteAllText(paths.PendingFile, """{"ToVersion":"2.3.0"}""");

        UpdateStartup.Confirm(paths, "2.2.0");
        Assert.False(File.Exists(paths.OkMarker));

        UpdateStartup.Confirm(paths, "2.3.0");
        Assert.True(File.Exists(paths.OkMarker));
    }
}

public sealed class UpdateServiceTests : TempDirTest
{
    private const string Json = """
    [
      {"tag_name":"v2.4.0-beta.1","prerelease":true,"assets":[{"name":"HiveShock-2.4.0-beta.1-win-x64-update.zip","browser_download_url":"https://x/b.zip","size":1}]},
      {"tag_name":"v2.3.0","assets":[{"name":"HiveShock-2.3.0-win-x64-update.zip","browser_download_url":"https://x/a.zip","size":1}]},
      {"tag_name":"v2.2.0","assets":[]}
    ]
    """;

    private UpdateService Make(UpdateOptions options, string current = "2.2.0", string json = Json)
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });
        return new UpdateService(new HttpClient(handler), current, options,
            new UpdatePaths(Path.Combine(Dir, "upd")), Path.Combine(Dir, "app"), "win-x64");
    }

    [Fact]
    public async Task Offers_latest_stable_by_default()
    {
        using var svc = Make(new UpdateOptions());
        await svc.CheckAsync();
        Assert.Equal(UpdateState.Available, svc.State);
        Assert.Equal("2.3.0", svc.Latest!.Version);
    }

    [Fact]
    public async Task Check_announces_but_never_downloads_on_its_own()
    {
        var requests = new List<string>();
        var handler = new FakeHandler(r =>
        {
            requests.Add(r.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Json, Encoding.UTF8, "application/json"),
            };
        });
        using var svc = new UpdateService(new HttpClient(handler), "2.2.0", new UpdateOptions(),
            new UpdatePaths(Path.Combine(Dir, "upd")), Path.Combine(Dir, "app"), "win-x64");

        await svc.CheckAsync(silent: true);

        Assert.Equal(UpdateState.Available, svc.State);
        Assert.Null(svc.Downloaded);
        Assert.Single(requests);
        Assert.False(Directory.Exists(Path.Combine(Dir, "upd", "downloads")));
    }

    [Fact]
    public async Task Prerelease_channel_offers_beta()
    {
        using var svc = Make(new UpdateOptions { IncludePrereleases = true });
        await svc.CheckAsync();
        Assert.Equal("2.4.0-beta.1", svc.Latest!.Version);
    }

    [Fact]
    public async Task Up_to_date_when_on_latest()
    {
        using var svc = Make(new UpdateOptions(), current: "2.3.0");
        await svc.CheckAsync();
        Assert.Equal(UpdateState.UpToDate, svc.State);
        Assert.Null(svc.Latest);
    }

    [Fact]
    public async Task Skipped_version_is_not_announced()
    {
        using var svc = Make(new UpdateOptions { SkippedVersion = "2.3.0" });
        await svc.CheckAsync();
        Assert.NotNull(svc.Latest);
        Assert.Null(svc.Announceable);
    }

    [Fact]
    public async Task Silent_check_hides_network_errors()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var svc = new UpdateService(new HttpClient(handler), "2.2.0", new UpdateOptions(),
            new UpdatePaths(Path.Combine(Dir, "upd")), Path.Combine(Dir, "app"), "win-x64");
        await svc.CheckAsync(silent: true);
        Assert.NotEqual(UpdateState.Failed, svc.State);
        await svc.CheckAsync(silent: false);
        Assert.Equal(UpdateState.Failed, svc.State);
    }
}
