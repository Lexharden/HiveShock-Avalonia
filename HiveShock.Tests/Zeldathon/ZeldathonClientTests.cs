using System.Text.Json.Nodes;
using HiveShock.Zeldathon;

namespace HiveShock.Tests.Zeldathon;

public class ZeldathonClientTests
{
    private static readonly Uri Server = new("wss://zeldathon.example.com/ingest");

    private const string ClockPush = """
        {"type":"CLOCK","clock":{"racerId":"ralbat","remainingMs":14340000,
        "resetAtUtc":"2026-10-08T12:00:00.000Z","serverTimeUtc":"2026-10-07T13:01:00.000Z","status":"live"}}
        """;

    private static ZeldathonClientOptions Fast() => new()
    {
        HeartbeatInterval = TimeSpan.FromHours(1),
        MinBackoff = TimeSpan.FromMilliseconds(1),
        MaxBackoff = TimeSpan.FromMilliseconds(5),
        ClientVersion = "test/1.0",
    };

    private static async Task WaitUntil(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Nunca se cumplió: {what}");
            }

            await Task.Delay(5);
        }
    }

    private static string IdOf(string json) => JsonNode.Parse(json)!["id"]!.GetValue<string>();

    private static string TypeOf(string json) => JsonNode.Parse(json)!["type"]!.GetValue<string>();

    [Fact]
    public async Task Says_hello_first_and_takes_the_official_clock()
    {
        var factory = new FakeTransportFactory
        {
            Configure = (_, t) => t.OnSent = (self, text) =>
            {
                if (TypeOf(text) == "HELLO")
                {
                    self.Push(ClockPush);
                }
            },
        };
        var clock = new ZeldathonClock(() => 0);
        await using var client = new ZeldathonClient(factory, clock, Fast());
        client.Start(Server, "tok");

        await WaitUntil(() => clock.HasValue, "llega el reloj");
        var first = factory.Created[0].Sent[0];
        Assert.Equal("HELLO", TypeOf(first));
        Assert.Contains("test/1.0", first);
        Assert.Equal(14_340_000, clock.Snapshot!.RemainingMs);
        Assert.Equal(ZeldathonConnectionState.Connected, client.State);
    }

    [Fact]
    public async Task Resends_only_unconfirmed_messages_after_reconnecting()
    {
        var factory = new FakeTransportFactory();
        var clock = new ZeldathonClock(() => 0);
        await using var client = new ZeldathonClient(factory, clock, Fast());
        client.Start(Server, "tok");
        await WaitUntil(() => factory.Created.Count == 1, "primera conexión");

        var confirmed = ZeldathonProtocol.Message("ITEM_ACQUIRED", fields: new JsonObject { ["item"] = "hookshot" });
        var lost = ZeldathonProtocol.Message("ITEM_ACQUIRED", fields: new JsonObject { ["item"] = "bow" });
        client.Send(confirmed);
        client.Send(lost);
        await WaitUntil(() => factory.Created[0].Sent.Count(s => TypeOf(s) == "ITEM_ACQUIRED") == 2, "enviados");

        factory.Created[0].Push($$"""{"type":"ACK","id":"{{confirmed.Id}}"}""");
        await WaitUntil(() => client.Acked == 1, "ack recibido");
        factory.Created[0].Close();

        await WaitUntil(() => factory.Created.Count == 2, "reconecta");
        await WaitUntil(() => factory.Created[1].Sent.Count(s => TypeOf(s) == "ITEM_ACQUIRED") == 1, "reenvío");
        var resent = factory.Created[1].Sent.Single(s => TypeOf(s) == "ITEM_ACQUIRED");
        Assert.Equal(lost.Id, IdOf(resent));
        Assert.Equal("HELLO", TypeOf(factory.Created[1].Sent[0]));
    }

    [Fact]
    public async Task A_rejected_token_is_not_retried()
    {
        var factory = new FakeTransportFactory
        {
            Configure = (_, t) => t.FailConnect = new ZeldathonAuthException("401"),
        };
        var clock = new ZeldathonClock(() => 0);
        await using var client = new ZeldathonClient(factory, clock, Fast());
        client.Start(Server, "malo");

        await WaitUntil(() => client.State == ZeldathonConnectionState.AuthFailed, "AuthFailed");
        await Task.Delay(50);
        Assert.Single(factory.Created);
        Assert.Contains("token", client.LastError);
    }

    [Fact]
    public async Task Reconnects_with_backoff_after_network_failures()
    {
        var factory = new FakeTransportFactory
        {
            Configure = (i, t) =>
            {
                if (i < 2)
                {
                    t.FailConnect = new IOException("sin red");
                }
            },
        };
        var clock = new ZeldathonClock(() => 0);
        await using var client = new ZeldathonClient(factory, clock, Fast());
        client.Start(Server, "tok");

        await WaitUntil(() => factory.Created.Count >= 3 && client.State == ZeldathonConnectionState.Connected, "tercera conexión");
        Assert.Equal("", client.LastError);
    }

    [Fact]
    public async Task Another_connection_taking_over_stops_the_client()
    {
        var factory = new FakeTransportFactory
        {
            Configure = (_, t) => t.OnSent = (self, text) =>
            {
                if (TypeOf(text) == "HELLO")
                {
                    self.Push("""{"type":"ERROR","code":"replaced","message":"another connection took over"}""");
                }
            },
        };
        var rejected = new List<string>();
        var clock = new ZeldathonClock(() => 0);
        await using var client = new ZeldathonClient(factory, clock, Fast());
        client.MessageRejected += (_, code, _) => rejected.Add(code);
        client.Start(Server, "tok");

        await WaitUntil(() => client.State == ZeldathonConnectionState.Replaced, "Replaced");
        await Task.Delay(50);
        Assert.Single(factory.Created);
        Assert.Contains("replaced", rejected);
    }

    [Fact]
    public async Task Force_close_from_the_server_is_raised_and_the_connection_stays_up()
    {
        var factory = new FakeTransportFactory();
        var clock = new ZeldathonClock(() => 0);
        await using var client = new ZeldathonClient(factory, clock, Fast());
        var closes = 0;
        client.ForceCloseRequested += () => Interlocked.Increment(ref closes);
        client.Start(Server, "tok");
        await WaitUntil(() => factory.Created.Count == 1 && factory.Created[0].Sent.Count == 1, "hello");

        factory.Created[0].Push("""{"type":"GAME_FORCE_CLOSE"}""");
        await WaitUntil(() => closes == 1, "force close");
        Assert.Equal(ZeldathonConnectionState.Connected, client.State);
    }

    [Fact]
    public async Task Sends_heartbeats_with_unique_ids_and_the_game_state()
    {
        var factory = new FakeTransportFactory();
        var clock = new ZeldathonClock(() => 0);
        var options = Fast();
        options.HeartbeatInterval = TimeSpan.FromMilliseconds(10);
        await using var client = new ZeldathonClient(factory, clock, options) { GameRunning = () => true };
        client.Start(Server, "tok");

        await WaitUntil(() => factory.Created.Count == 1 && factory.Created[0].Sent.Count(s => TypeOf(s) == "HEARTBEAT") >= 3, "latidos");
        var beats = factory.Created[0].Sent.Where(s => TypeOf(s) == "HEARTBEAT").ToList();
        Assert.Equal(beats.Count, beats.Select(IdOf).Distinct().Count());
        Assert.All(beats, b => Assert.Contains("\"gameRunning\":true", b));
    }

    [Fact]
    public async Task Rejected_messages_are_dropped_and_explained()
    {
        var factory = new FakeTransportFactory();
        var clock = new ZeldathonClock(() => 0);
        await using var client = new ZeldathonClient(factory, clock, Fast());
        var seen = new List<(string Type, string Code)>();
        client.MessageRejected += (type, code, _) => seen.Add((type, code));
        client.Start(Server, "tok");
        await WaitUntil(() => factory.Created.Count == 1, "conexión");

        var msg = ZeldathonProtocol.Message("SESSION_STARTED");
        client.Send(msg);
        await WaitUntil(() => factory.Created[0].Sent.Any(s => TypeOf(s) == "SESSION_STARTED"), "enviado");
        factory.Created[0].Push($$"""{"type":"ERROR","id":"{{msg.Id}}","code":"event_not_live","message":"the event is not live"}""");
        await WaitUntil(() => seen.Count == 1, "rechazo");

        Assert.Equal(("SESSION_STARTED", "event_not_live"), seen[0]);
        Assert.Contains("todavía no", client.LastError);
        factory.Created[0].Close();
        await WaitUntil(() => factory.Created.Count == 2, "reconecta");
        await Task.Delay(50);
        Assert.DoesNotContain(factory.Created[1].Sent, s => TypeOf(s) == "SESSION_STARTED");
    }

    [Fact]
    public async Task Stop_ends_the_loop_and_freezes_the_clock()
    {
        var factory = new FakeTransportFactory();
        var clock = new ZeldathonClock(() => 0);
        clock.Update(new ZeldathonClockSnapshot("r", 1000, ZeldathonRacerStatus.Live, DateTime.UtcNow, DateTime.UtcNow));
        var client = new ZeldathonClient(factory, clock, Fast());
        client.Start(Server, "tok");
        await WaitUntil(() => factory.Created.Count == 1, "conexión");

        client.Stop();
        Assert.Equal(ZeldathonConnectionState.Stopped, client.State);
        Assert.True(clock.IsDisconnected);
        Assert.True(factory.Created[0].Disposed);
    }
}
