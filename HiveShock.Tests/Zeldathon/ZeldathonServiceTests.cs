using HiveShock.Zeldathon;

namespace HiveShock.Tests.Zeldathon;

public class ZeldathonServiceTests
{
    [Fact]
    public void Connect_reports_readable_errors_instead_of_starting()
    {
        var factory = new FakeTransportFactory();
        var noToken = new ZeldathonService(new ZeldathonSettings { ServerUrl = "zeldathon.example.com" }, factory);
        Assert.Contains("token", noToken.Connect(), StringComparison.OrdinalIgnoreCase);

        var plain = new ZeldathonService(new ZeldathonSettings { ServerUrl = "http://zeldathon.example.com", Token = "t" }, factory);
        Assert.Contains("cifrada", plain.Connect());
        Assert.Empty(factory.Created);
    }

    [Fact]
    public async Task Reads_the_budget_and_status_of_the_event()
    {
        Uri? asked = null;
        var service = new ZeldathonService(
            new ZeldathonSettings { ServerUrl = "https://zeldathon.example.com", Token = "t" },
            new FakeTransportFactory(),
            fetch: (uri, _) =>
            {
                asked = uri;
                return Task.FromResult("""{"status":"live","dailyBudgetSeconds":10800}""");
            });

        await service.RefreshEventAsync();
        Assert.Equal("https://zeldathon.example.com/api/event", asked!.ToString());
        Assert.Equal(10_800, service.BudgetSeconds);
        Assert.Equal("live", service.EventStatus);
    }

    [Fact]
    public async Task A_failing_event_lookup_keeps_the_default_budget()
    {
        var service = new ZeldathonService(
            new ZeldathonSettings { ServerUrl = "https://zeldathon.example.com", Token = "t" },
            new FakeTransportFactory(),
            fetch: (_, _) => throw new HttpRequestException("caído"));
        await service.RefreshEventAsync();
        Assert.Equal(ZeldathonService.DefaultBudgetSeconds, service.BudgetSeconds);
    }

    [Fact]
    public async Task Auto_connect_only_when_enabled_and_configured()
    {
        var factory = new FakeTransportFactory();
        await using var off = new ZeldathonService(new ZeldathonSettings { Enabled = false, ServerUrl = "zeldathon.example.com", Token = "t" }, factory);
        off.AutoConnectIfConfigured();
        await Task.Delay(50);
        Assert.Empty(factory.Created);

        await using var on = new ZeldathonService(new ZeldathonSettings { Enabled = true, ServerUrl = "zeldathon.example.com", Token = "t" }, factory, fetch: (_, _) => Task.FromResult("{}"));
        on.AutoConnectIfConfigured();
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (factory.Created.Count == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.Single(factory.Created);
    }
}
