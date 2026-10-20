using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using HiveShock.Configuration;
using HiveShock.Networking;

namespace HiveShock.Tests.Live;

/// <summary>Un efecto pagado no se pierde si el juego está cerrado un momento: el despachador reintenta.</summary>
public sealed class EffectDispatcherRetryTests
{
    private static EffectCatalog Catalog()
    {
        Dictionary<string, JsonElement> Effect(string json) => JsonDocument.Parse(json).RootElement
            .EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());

        return new EffectCatalog(new Dictionary<string, Dictionary<string, JsonElement>>
        {
            ["impulse"] = Effect("""{ "action": "impulse" }"""),
            ["heal"] = Effect("""{ "action": "heal" }"""),
        });
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static async Task<string> ReadOneLineAsync(TcpListener listener, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        using var client = await listener.AcceptTcpClientAsync(cts.Token);
        using var reader = new StreamReader(client.GetStream(), Encoding.UTF8);
        return await reader.ReadLineAsync(cts.Token) ?? "";
    }

    [Fact]
    public async Task Effect_is_delivered_after_the_game_comes_back()
    {
        var port = FreePort();
        var options = new BridgeOptions
        {
            GameHost = "127.0.0.1",
            GamePort = port,
            EffectGapMs = 0,
            GameConnectTimeout = TimeSpan.FromSeconds(1),
            GameRetryWindow = TimeSpan.FromSeconds(30),
        };
        var effects = Catalog();
        var dispatcher = new EffectDispatcher(effects, new GameTcpClient(options));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var pump = dispatcher.RunAsync(cts.Token);

        await dispatcher.EnqueueAsync("impulse", "retry_viewer", "test", cts.Token);

        // The game is "closed" for a while, then opens.
        await Task.Delay(1500, cts.Token);
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        try
        {
            var line = await ReadOneLineAsync(listener, TimeSpan.FromSeconds(15));
            Assert.Contains("impulse", line);
        }
        finally
        {
            listener.Stop();
            cts.Cancel();
            await pump;
        }
    }

    [Fact]
    public async Task Effect_is_dropped_once_the_retry_window_is_over_and_the_queue_keeps_moving()
    {
        var port = FreePort();
        var options = new BridgeOptions
        {
            GameHost = "127.0.0.1",
            GamePort = port,
            EffectGapMs = 0,
            GameConnectTimeout = TimeSpan.FromSeconds(1),
            GameRetryWindow = TimeSpan.FromSeconds(1),
        };
        var effects = Catalog();
        var dispatcher = new EffectDispatcher(effects, new GameTcpClient(options));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var pump = dispatcher.RunAsync(cts.Token);

        await dispatcher.EnqueueAsync("impulse", "too_late", "test", cts.Token);

        // Wait past the window so the first effect is given up, then bring the game back and send another one.
        await Task.Delay(4000, cts.Token);
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        try
        {
            await dispatcher.EnqueueAsync("heal", "on_time", "test", cts.Token);
            var line = await ReadOneLineAsync(listener, TimeSpan.FromSeconds(15));
            Assert.Contains("heal", line);
            Assert.DoesNotContain("impulse", line);
        }
        finally
        {
            listener.Stop();
            cts.Cancel();
            await pump;
        }
    }

    [Fact]
    public async Task Without_a_retry_window_a_failed_effect_is_not_retried()
    {
        var port = FreePort();
        var options = new BridgeOptions
        {
            GameHost = "127.0.0.1",
            GamePort = port,
            EffectGapMs = 0,
            GameConnectTimeout = TimeSpan.FromSeconds(1),
            GameRetryWindow = TimeSpan.Zero,
        };
        var effects = Catalog();
        var dispatcher = new EffectDispatcher(effects, new GameTcpClient(options));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var pump = dispatcher.RunAsync(cts.Token);

        await dispatcher.EnqueueAsync("impulse", "no_retry", "test", cts.Token);
        await Task.Delay(1500, cts.Token);

        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        try
        {
            await Task.Delay(1500, cts.Token);
            Assert.False(listener.Pending(), "the failed effect must not be redelivered");
        }
        finally
        {
            listener.Stop();
            cts.Cancel();
            await pump;
        }
    }

    [Fact]
    public void Spawn_queue_event_is_parsed()
    {
        using var doc = JsonDocument.Parse(
            """{ "event": "spawn_queue", "pending": 12, "active": 5, "load": 11, "max": 14 }""");

        Assert.True(GameSpawnQueue.TryParse(doc.RootElement, out var queue));
        Assert.Equal(new GameSpawnQueue(12, 5, 11, 14), queue);
    }

    [Fact]
    public void Spawn_queue_event_with_missing_fields_defaults_to_zero()
    {
        using var doc = JsonDocument.Parse("""{ "event": "spawn_queue", "pending": 3 }""");

        Assert.True(GameSpawnQueue.TryParse(doc.RootElement, out var queue));
        Assert.Equal(new GameSpawnQueue(3, 0, 0, 0), queue);
    }
}
