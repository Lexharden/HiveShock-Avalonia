using System.Net;
using System.Text.Json;
using HiveShock.Configuration;
using HiveShock.Live;
using HiveShock.Overlays;

namespace HiveShock.Tests.Overlays;

public sealed class WebOverlayServerTests : IDisposable
{
    private readonly WebOverlayServer _server = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hiveshock-web-" + Guid.NewGuid().ToString("N"));

    public WebOverlayServerTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        _http.Dispose();
        _server.Dispose();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Url(string path) => $"http://127.0.0.1:{_server.Port}{path}";

    private async Task<List<string>> ReadEventsAsync(string path, int frames, Action? afterConnect = null)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var response = await _http.GetAsync(Url(path), HttpCompletionOption.ResponseHeadersRead, cts.Token);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        afterConnect?.Invoke();
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cts.Token));
        var seen = new List<string>();
        while (seen.Count < frames)
        {
            var line = await reader.ReadLineAsync(cts.Token);
            Assert.NotNull(line);
            if (line.StartsWith("event: ", StringComparison.Ordinal) || line.StartsWith("data: ", StringComparison.Ordinal))
            {
                seen.Add(line);
            }
        }

        return seen;
    }

    [Fact]
    public async Task Serves_the_overlay_pages_and_404s_everything_else()
    {
        Assert.True(_server.Start(0, allowLan: false, token: null));

        foreach (var name in new[] { "contador", "regalos", "metas", "alertas" })
        {
            var response = await _http.GetAsync(Url("/overlay/" + name));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("EventSource", await response.Content.ReadAsStringAsync());
        }

        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync(Url("/otra-cosa"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync(Url("/img/../secreto.txt"))).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await _http.PostAsync(Url("/events"), new StringContent(""))).StatusCode);
    }

    [Fact]
    public async Task A_new_client_gets_the_current_state_and_then_live_updates()
    {
        Assert.True(_server.Start(0, false, null));
        _server.Publish("counter", """{"title":"Muertes","value":3}""");

        var frames = await ReadEventsAsync("/events", 4, () =>
        {
            _server.Publish("counter", """{"title":"Muertes","value":4}""");
        });

        Assert.Equal("event: counter", frames[0]);
        Assert.Equal("""data: {"title":"Muertes","value":3}""", frames[1]);
        Assert.Equal("event: counter", frames[2]);
        Assert.Equal("""data: {"title":"Muertes","value":4}""", frames[3]);
    }

    [Fact]
    public async Task Alerts_are_pushed_to_connected_pages_but_not_replayed_to_new_ones()
    {
        Assert.True(_server.Start(0, false, null));
        _server.Push("alert", """{"kind":"gift"}""");

        var frames = await ReadEventsAsync("/events", 2, () => _server.Push("alert", """{"kind":"follow"}"""));

        Assert.Equal("""data: {"kind":"follow"}""", frames[1]);
    }

    [Fact]
    public async Task With_a_token_every_request_must_carry_it()
    {
        Assert.True(_server.Start(0, allowLan: true, token: "secreto"));

        Assert.Equal(HttpStatusCode.Forbidden, (await _http.GetAsync(Url("/overlay/metas"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.GetAsync(Url("/overlay/metas?k=otro"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _http.GetAsync(Url("/overlay/metas?k=secreto"))).StatusCode);
        Assert.EndsWith("/overlay/metas?k=secreto", _server.UrlFor("metas"));
    }

    [Fact]
    public async Task Only_registered_images_are_served()
    {
        Assert.True(_server.Start(0, false, null));
        var png = Path.Combine(_dir, "rosa.png");
        File.WriteAllBytes(png, [0x89, 0x50, 0x4E, 0x47]);
        var secret = Path.Combine(_dir, "datos.txt");
        File.WriteAllText(secret, "no");

        var url = _server.RegisterImage(png);
        Assert.NotNull(url);
        Assert.Null(_server.RegisterImage(secret)); // no es una imagen
        Assert.Null(_server.RegisterImage(Path.Combine(_dir, "no-existe.png")));

        var response = await _http.GetAsync(Url(url!));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(4, (await response.Content.ReadAsByteArrayAsync()).Length);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync(Url("/img/desconocida.png"))).StatusCode);
    }

    [Fact]
    public void An_occupied_port_is_reported_instead_of_throwing()
    {
        Assert.True(_server.Start(0, false, null));
        using var other = new WebOverlayServer();

        Assert.False(other.Start(_server.Port, false, null));
        Assert.False(other.IsRunning);
    }

    [Fact]
    public async Task The_feed_describes_gifts_goals_and_alerts_for_the_pages()
    {
        Assert.True(_server.Start(0, false, null));
        var image = Path.Combine(_dir, "donut.webp");
        File.WriteAllBytes(image, [1, 2, 3]);
        var feed = new OverlayFeed(_server, key => key == "Doughnut" ? new OverlayGiftRef("Doughnut", image) : null);

        feed.SetLook(OverlayLookData.Default);
        feed.SetCounter("Vidas", 497);
        feed.SetGifts([
            new EditableGift { Gift = "Rose", Id = "rose", Overlay = true, OverlayText = "Rosa" },
            new EditableGift { Gift = "Oculto", Overlay = false },
        ]);
        feed.SetGoals([new GoalSnapshot { Id = "g1", Label = "Donas", Count = 2, Need = 5, Keys = ["Doughnut", "Sin catalogo"] }]);

        var frames = await ReadEventsAsync("/events", 8);
        var data = frames.Where(f => f.StartsWith("data: ", StringComparison.Ordinal))
            .Select(f => JsonDocument.Parse(f[6..]).RootElement.Clone()).ToList();

        var gifts = data.First(d => d.TryGetProperty("items", out var i) && i.GetArrayLength() == 1 && i[0].TryGetProperty("label", out _));
        Assert.Equal("Rosa", gifts.GetProperty("items")[0].GetProperty("label").GetString());

        var goals = data.First(d => d.TryGetProperty("items", out var i) && i[0].TryGetProperty("need", out _));
        var goal = goals.GetProperty("items")[0];
        Assert.Equal("2/5", goal.GetProperty("progress").GetString());
        Assert.Equal(2, goal.GetProperty("gifts").GetArrayLength());
        var withImage = goal.GetProperty("gifts")[0].GetProperty("image").GetString();
        Assert.StartsWith("/img/", withImage);
        Assert.Equal(HttpStatusCode.OK, (await _http.GetAsync(Url(withImage!))).StatusCode);
        Assert.Equal(JsonValueKind.Null, goal.GetProperty("gifts")[1].GetProperty("image").ValueKind);
    }
}
