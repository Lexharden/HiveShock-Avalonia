using System.Text.Json;
using System.Text.Json.Nodes;
using HiveShock.Zeldathon;

namespace HiveShock.Tests.Zeldathon;

public class ZeldathonCatalogTests
{
    private const string ServerCatalog = """
        {
          "version": "abc123def456",
          "items": [
            { "id": "kokiri-sword", "age": "child", "enabled": true },
            { "id": "magic-beans", "age": "child", "enabled": true },
            { "id": "hidden-thing", "age": "both", "enabled": false }
          ],
          "objectives": [
            { "id": "water-temple", "age": "adult", "sortOrder": 70, "required": true, "enabled": true },
            { "id": "deku-tree", "age": "child", "sortOrder": 20, "required": true, "enabled": true },
            { "id": "bonus-goal", "age": "child", "sortOrder": 25, "required": false, "enabled": true },
            { "id": "old-goal", "age": "adult", "sortOrder": 1, "required": true, "enabled": false }
          ]
        }
        """;

    [Fact]
    public void The_built_in_catalog_matches_the_factory_one()
    {
        var c = ZeldathonCatalog.Default;
        Assert.True(c.Items.Count >= 60);
        Assert.Equal(10, c.Objectives.Count);
        Assert.Equal("kokiri-forest", c.Objectives[0]);
        Assert.Equal("ganons-castle", c.Objectives[^1]);
        foreach (var original in new[] { "master-sword", "hookshot", "longshot", "bow", "bombs", "boomerang", "megaton-hammer", "iron-boots", "mirror-shield" })
        {
            Assert.True(c.IsItem(original), original);
        }
    }

    [Fact]
    public void Parses_the_server_catalog_keeping_only_enabled_entries_in_race_order()
    {
        var c = ZeldathonCatalog.Parse(ServerCatalog);
        Assert.Equal("abc123def456", c.Version);
        Assert.True(c.IsItem("magic-beans"));
        Assert.False(c.IsItem("hidden-thing"));
        Assert.Equal(["deku-tree", "bonus-goal", "water-temple"], c.Objectives.ToArray());
        Assert.Equal(["deku-tree", "water-temple"], c.Required.ToArray());
        Assert.False(c.IsObjective("old-goal"));
    }

    [Fact]
    public void An_empty_or_broken_catalog_is_an_error_so_the_previous_one_stays()
    {
        Assert.Throws<JsonException>(() => ZeldathonCatalog.Parse("""{"items":[],"objectives":[]}"""));
        Assert.ThrowsAny<JsonException>(() => ZeldathonCatalog.Parse("not json"));
    }

    // ---- the service downloads it -------------------------------------------------------------------

    private static ZeldathonService Make(Func<Uri, CancellationToken, Task<string>> fetch) => new(
        new ZeldathonSettings { ServerUrl = "https://zeldathon.example.com", Token = "t" },
        new FakeTransportFactory(),
        fetch: fetch);

    [Fact]
    public async Task The_service_adopts_the_server_catalog_and_the_events_required_objectives()
    {
        var asked = new List<string>();
        await using var service = Make((uri, _) =>
        {
            asked.Add(uri.AbsolutePath);
            return Task.FromResult(uri.AbsolutePath == "/api/catalog"
                ? ServerCatalog
                : """{"status":"live","dailyBudgetSeconds":14400,"rules":{"requiredObjectiveIds":["deku-tree"]}}""");
        });

        await service.RefreshCatalogAsync();
        await service.RefreshEventAsync();

        Assert.Contains("/api/catalog", asked);
        Assert.Equal("abc123def456", service.Catalog.Version);
        Assert.True(service.Catalog.IsItem("magic-beans"));
        Assert.Equal(["deku-tree"], service.Session.RequiredObjectives!.ToArray());
    }

    [Fact]
    public async Task A_failing_catalog_download_keeps_the_factory_catalog()
    {
        await using var service = Make((_, _) => throw new HttpRequestException("caído"));
        await service.RefreshCatalogAsync();
        Assert.Equal("factory", service.Catalog.Version);

        await using var broken = Make((_, _) => Task.FromResult("""{"items":[],"objectives":[]}"""));
        await broken.RefreshCatalogAsync();
        Assert.Equal("factory", broken.Catalog.Version);
    }

    [Fact]
    public async Task The_same_catalog_version_is_not_reapplied()
    {
        var calls = 0;
        await using var service = Make((_, _) =>
        {
            calls++;
            return Task.FromResult(ServerCatalog);
        });
        await service.RefreshCatalogAsync();
        var first = service.Catalog;
        await service.RefreshCatalogAsync();
        Assert.Same(first, service.Catalog);
        Assert.Equal(2, calls);
    }
}
