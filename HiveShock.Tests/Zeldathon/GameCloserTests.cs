using System.Text.Json;
using HiveShock.Zeldathon;

namespace HiveShock.Tests.Zeldathon;

public class GameCloserTests
{
    private sealed class FakeProcesses : IProcessControl
    {
        public HashSet<int> Alive { get; } = [];
        public List<int> Killed { get; } = [];
        public bool RefuseToDie { get; set; }
        public Action? OnPoll { get; set; }

        public IReadOnlyList<int> Find(IEnumerable<string> names) => Alive.ToList();

        public bool IsAlive(int pid)
        {
            OnPoll?.Invoke();
            return Alive.Contains(pid);
        }

        public void Kill(int pid)
        {
            Killed.Add(pid);
            if (!RefuseToDie)
            {
                Alive.Remove(pid);
            }
        }
    }

    private static GameCloser Make(FakeProcesses p, Func<CancellationToken, Task> quit, string[]? names = null) =>
        new(quit, () => names ?? ["soh"], p, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), (_, _) => Task.CompletedTask);

    [Fact]
    public async Task A_game_that_closes_by_itself_is_left_alone()
    {
        var p = new FakeProcesses { Alive = { 10 } };
        var asked = 0;
        var closer = Make(p, _ =>
        {
            asked++;
            p.Alive.Clear();
            return Task.CompletedTask;
        });
        Assert.Equal(GameCloseOutcome.Graceful, await closer.CloseAsync());
        Assert.Equal(1, asked);
        Assert.Empty(p.Killed);
    }

    [Fact]
    public async Task A_game_that_ignores_the_request_is_killed_after_the_grace_period()
    {
        var p = new FakeProcesses { Alive = { 10, 11 } };
        var closer = Make(p, _ => Task.CompletedTask);
        Assert.Equal(GameCloseOutcome.Killed, await closer.CloseAsync());
        Assert.Equal([10, 11], p.Killed.Order().ToArray());
    }

    [Fact]
    public async Task Reports_failure_when_the_process_will_not_die()
    {
        var p = new FakeProcesses { Alive = { 10 }, RefuseToDie = true };
        var closer = Make(p, _ => Task.CompletedTask);
        var messages = new List<string>();
        closer.Finished += (_, m) => messages.Add(m);
        Assert.Equal(GameCloseOutcome.Failed, await closer.CloseAsync());
        Assert.Contains("Ciérralo tú", messages.Single());
    }

    [Fact]
    public async Task A_game_already_closed_is_not_touched()
    {
        var p = new FakeProcesses();
        var asked = 0;
        var closer = Make(p, _ =>
        {
            asked++;
            return Task.CompletedTask;
        });
        Assert.Equal(GameCloseOutcome.NotRunning, await closer.CloseAsync());
        Assert.Equal(0, asked);
    }

    [Fact]
    public async Task Without_a_known_process_it_only_asks_and_tells_the_user()
    {
        var p = new FakeProcesses();
        var asked = 0;
        var closer = Make(p, _ =>
        {
            asked++;
            return Task.CompletedTask;
        }, names: []);
        Assert.Equal(GameCloseOutcome.Unknown, await closer.CloseAsync());
        Assert.Equal(1, asked);
        Assert.Empty(p.Killed);
    }

    [Fact]
    public async Task A_failing_request_does_not_stop_the_forced_close()
    {
        var p = new FakeProcesses { Alive = { 10 } };
        var closer = Make(p, _ => throw new InvalidOperationException("juego sin conexión"));
        Assert.Equal(GameCloseOutcome.Killed, await closer.CloseAsync());
    }

    [Fact]
    public async Task A_second_order_while_closing_is_ignored()
    {
        var p = new FakeProcesses { Alive = { 10 } };
        var gate = new TaskCompletionSource();
        var closer = Make(p, async _ =>
        {
            await gate.Task;
            p.Alive.Clear();
        });
        var first = closer.CloseAsync();
        Assert.True(closer.IsClosing);
        Assert.Null(await closer.CloseAsync());
        gate.SetResult();
        Assert.Equal(GameCloseOutcome.Graceful, await first);
        Assert.False(closer.IsClosing);
    }

    [Fact]
    public async Task The_service_closes_the_game_when_the_server_orders_it_and_counts_chat()
    {
        var p = new FakeProcesses { Alive = { 42 } };
        var factory = new FakeTransportFactory();
        await using var service = new ZeldathonService(
            new ZeldathonSettings { ServerUrl = "https://zeldathon.example.com", Token = "t" },
            factory,
            new ZeldathonClientOptions { HeartbeatInterval = TimeSpan.FromHours(1), MinBackoff = TimeSpan.FromMilliseconds(1) },
            (_, _) => Task.FromResult("{}"),
            p);
        service.Session.Map = ZeldathonMap.Parse("""{"gameProcesses":["soh"]}""");
        var quit = 0;
        service.RequestGameQuit = _ =>
        {
            quit++;
            p.Alive.Clear();
            return Task.CompletedTask;
        };
        var notices = new List<string>();
        service.Notice += notices.Add;

        Assert.Null(service.Connect());
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while ((factory.Created.Count == 0 || factory.Created[0].Sent.Count == 0) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        factory.Created[0].Push("""{"type":"GAME_FORCE_CLOSE"}""");
        deadline = DateTime.UtcNow.AddSeconds(3);
        while (notices.Count == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.Equal(1, quit);
        Assert.Contains("se cerró", notices.Single());

        service.CountChat(3);
        service.CountChat(2);
        service.Tick();
        deadline = DateTime.UtcNow.AddSeconds(3);
        while (!factory.Created[0].Sent.Any(s => s.Contains("CHAT_EVENT")) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        var chat = factory.Created[0].Sent.Single(s => s.Contains("CHAT_EVENT"));
        Assert.Contains("\"count\":5", chat);
    }

    [Fact]
    public async Task A_game_that_vanishes_ends_the_session_after_two_checks()
    {
        var p = new FakeProcesses { Alive = { 7 } };
        await using var service = new ZeldathonService(
            new ZeldathonSettings { ServerUrl = "https://zeldathon.example.com", Token = "t" },
            new FakeTransportFactory(),
            processes: p);
        service.Session.Map = ZeldathonMap.Parse("""{"gameProcesses":["soh"]}""");
        using var doc = JsonDocument.Parse("""{"event":"game_session","state":"loaded"}""");
        service.Session.OnGameEvent("game_session", doc.RootElement.Clone());
        Assert.True(service.Session.GameActive);

        service.Tick();
        Assert.True(service.Session.GameActive);

        p.Alive.Clear();
        service.Tick();
        Assert.True(service.Session.GameActive); // una sola revisión sin él: puede ser un parpadeo
        service.Tick();
        Assert.False(service.Session.GameActive);
    }
}
