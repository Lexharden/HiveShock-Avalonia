using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using HiveShock.Zeldathon;

namespace HiveShock.Tests.Zeldathon;

/// <summary>
/// Prueba de punta a punta contra un servidor de Zeldathon REAL (el del repo Zeldaton-web en local). Solo corre
/// si están estas variables; si no, pasa sin hacer nada:
///   ZELDATHON_E2E_URL=http://127.0.0.1:8080  ZELDATHON_E2E_TOKEN=&lt;token de "cuaco"&gt;  ZELDATHON_E2E_ADMIN=&lt;ADMIN_TOKEN&gt;
/// Cambia el estado del evento y del corredor "cuaco": usar solo con una base de datos de desarrollo.
/// </summary>
public class ZeldathonE2ETests
{
    private static readonly string? Url = Environment.GetEnvironmentVariable("ZELDATHON_E2E_URL");
    private static readonly string? Token = Environment.GetEnvironmentVariable("ZELDATHON_E2E_TOKEN");
    private static readonly string? Admin = Environment.GetEnvironmentVariable("ZELDATHON_E2E_ADMIN");

    private static bool Enabled => !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrWhiteSpace(Token) && !string.IsNullOrWhiteSpace(Admin);

    private static async Task WaitUntil(Func<Task<bool>> condition, string what, int seconds = 10)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Nunca se cumplió: {what}");
            }

            await Task.Delay(100);
        }
    }

    [Fact]
    public async Task A_racer_plays_end_to_end_against_the_real_server()
    {
        if (!Enabled)
        {
            return;
        }

        using var http = new HttpClient { BaseAddress = new Uri(Url!.TrimEnd('/') + "/") };
        var adminHeader = new AuthenticationHeaderValue("Bearer", Admin);
        async Task<HttpResponseMessage> AdminCall(HttpMethod method, string path, object body)
        {
            var req = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
            req.Headers.Authorization = adminHeader;
            return await http.SendAsync(req);
        }

        async Task<JsonNode> Get(string path) => JsonNode.Parse(await http.GetStringAsync(path))!;

        (await AdminCall(HttpMethod.Put, "api/admin/event", new { status = "live" })).EnsureSuccessStatusCode();
        // Deja al corredor con el día completo por si otra prueba lo tocó.
        (await AdminCall(HttpMethod.Post, "api/admin/racers/cuaco/actions/reset-day", new { reason = "e2e" })).EnsureSuccessStatusCode();

        var settings = new ZeldathonSettings { ServerUrl = Url!, Token = Token! };
        await using var service = new ZeldathonService(settings, options: new ZeldathonClientOptions { HeartbeatInterval = TimeSpan.FromSeconds(2) });
        var closed = new List<string>();
        service.Notice += closed.Add;
        var quits = 0;
        service.RequestGameQuit = _ =>
        {
            quits++;
            return Task.CompletedTask;
        };
        service.Session.Map = ZeldathonMap.Parse("""
            {
              "areas": { "85": "kokiri-forest", "5": "water-temple" },
              "items": { "10": "hookshot", "3": "bow" },
              "bosses": { "40": "gohma" },
              "objectives": { "quest": { "18": "deku-tree" }, "items": { "59": "kokiri-forest" } }
            }
            """);
        var broadcasting = false;
        service.IsBroadcasting = () => broadcasting;

        Assert.Null(service.Connect());
        await WaitUntil(() => Task.FromResult(service.Clock.HasValue), "llega el reloj oficial");
        Assert.Equal(ZeldathonRacerStatus.Online, service.Clock.Status);
        Assert.InRange(service.Clock.RemainingMs(), 14_300_000, 14_400_000);
        await service.RefreshEventAsync();
        Assert.Equal(14_400, service.BudgetSeconds);
        Assert.Equal("live", service.EventStatus);

        // El juego carga una partida y manda su estado.
        void Game(string json)
        {
            using var doc = JsonDocument.Parse(json);
            service.Session.OnGameEvent(doc.RootElement.GetProperty("event").GetString()!, doc.RootElement.Clone());
        }

        Game("""{"event":"game_session","state":"loaded"}""");
        Game("""{"event":"scene","scene":85}""");
        Game("""{"event":"inventory","items":[10,3,59]}""");
        Game("""{"event":"quest","items":262144}""");
        Game("""{"event":"stats","hearts":7,"maxHearts":8,"rupees":150,"skulltulas":4}""");
        Game("""{"event":"boss_defeated","actor":40}""");

        await WaitUntil(async () => (await Get("api/racers/cuaco"))["status"]?.GetValue<string>() == "live", "la sesión queda en vivo");
        await WaitUntil(async () =>
        {
            var racer = await Get("api/racers/cuaco");
            return racer["items"]?["hookshot"]?.GetValue<bool>() == true
                   && racer["items"]?["bow"]?.GetValue<bool>() == true
                   && racer["stats"]?["rupees"]?.GetValue<long>() == 150
                   && racer["stats"]?["bossesDefeated"]?.GetValue<int>() == 1
                   && racer["currentArea"]?.GetValue<string>() == "kokiri-forest";
        }, "el progreso llega a la web");
        var progress = await Get("api/racers/cuaco");
        Assert.Equal(20.0, progress["progressPercentage"]!.GetValue<double>());
        Assert.Contains("deku-tree", progress["completedObjectives"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Contains("kokiri-forest", progress["completedObjectives"]!.AsArray().Select(n => n!.GetValue<string>()));

        // El reloj oficial cuenta y se ajusta desde el organizador sin esperar al siguiente latido.
        var before = service.Clock.RemainingMs();
        (await AdminCall(HttpMethod.Post, "api/admin/racers/cuaco/actions/adjust-time", new { deltaSeconds = -600, reason = "e2e" })).EnsureSuccessStatusCode();
        await WaitUntil(() => Task.FromResult(before - service.Clock.RemainingMs() >= 590_000), "el ajuste del organizador llega al reloj", seconds: 3);

        // Directo y espectadores.
        broadcasting = true;
        service.Stream.SetViewers(321);
        service.RefreshStream();
        await WaitUntil(async () =>
        {
            var streams = (await Get("api/streams")).AsArray();
            var mine = streams.FirstOrDefault(s => s?["racerId"]?.GetValue<string>() == "cuaco");
            return mine?["viewers"]?.GetValue<int>() == 321;
        }, "los espectadores llegan a la web");

        // Chat.
        service.CountChat(7);
        service.Tick();
        await WaitUntil(async () => (await Get("api/hiveshock/stats"))["chatEvents"]!.GetValue<long>() >= 7, "el chat cuenta");

        // El servidor ordena cerrar el juego.
        (await AdminCall(HttpMethod.Post, "api/admin/racers/cuaco/actions/force-close", new { reason = "e2e" })).EnsureSuccessStatusCode();
        await WaitUntil(() => Task.FromResult(quits == 1), "se pide el cierre limpio al juego", seconds: 3);

        service.Disconnect();
    }
}
