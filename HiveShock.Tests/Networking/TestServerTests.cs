using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using HiveShock.Configuration;
using HiveShock.Networking;

namespace HiveShock.Tests.Networking;

/// <summary>El puerto de pruebas recibe JSON por trozos de TCP: un carácter UTF-8 puede llegar partido.</summary>
public sealed class TestServerTests
{
    private static EffectCatalog Catalog() =>
        new(new Dictionary<string, Dictionary<string, JsonElement>>
        {
            ["impulse"] = JsonDocument.Parse("""{ "action": "impulse" }""").RootElement
                .EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()),
        });

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    [Fact]
    public async Task Multibyte_viewer_name_split_across_reads_arrives_intact()
    {
        const string viewer = "Ñandú 🎮";
        var gamePort = FreePort();
        var testPort = FreePort();
        var options = new BridgeOptions
        {
            GameHost = "127.0.0.1",
            GamePort = gamePort,
            TestPort = testPort,
            EffectGapMs = 0,
            GameConnectTimeout = TimeSpan.FromSeconds(2),
            GameRetryWindow = TimeSpan.Zero,
        };
        var effects = Catalog();
        var dispatcher = new EffectDispatcher(effects, new GameTcpClient(options));
        var server = new TestServer(options, effects, dispatcher);

        var game = new TcpListener(IPAddress.Loopback, gamePort);
        game.Start();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var pump = dispatcher.RunAsync(cts.Token);
        var serving = server.RunAsync(cts.Token);
        try
        {
            using var client = new TcpClient();
            await ConnectWithRetryAsync(client, testPort, cts.Token);
            var stream = client.GetStream();

            // A mano y no con JsonSerializer: este escaparía la «Ñ» como Ñ y no habría bytes multibyte.
            var bytes = Encoding.UTF8.GetBytes($$"""{"code":"impulse","viewer":"{{viewer}}"}""" + "\n");
            // Corta justo después del primer byte de «Ñ» (2 bytes en UTF-8).
            var cut = Array.IndexOf(bytes, (byte)0xC3) + 1;
            Assert.True(cut > 0);
            await stream.WriteAsync(bytes.AsMemory(0, cut), cts.Token);
            await stream.FlushAsync(cts.Token);
            await Task.Delay(200, cts.Token);
            await stream.WriteAsync(bytes.AsMemory(cut), cts.Token);
            await stream.FlushAsync(cts.Token);

            using var gameClient = await game.AcceptTcpClientAsync(cts.Token);
            using var reader = new StreamReader(gameClient.GetStream(), Encoding.UTF8);
            var line = await reader.ReadLineAsync(cts.Token) ?? "";

            using var doc = JsonDocument.Parse(line);
            Assert.Equal(viewer, doc.RootElement.GetProperty("user").GetString());
        }
        finally
        {
            game.Stop();
            cts.Cancel();
            await pump;
            await serving;
        }
    }

    private static async Task ConnectWithRetryAsync(TcpClient client, int port, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await client.ConnectAsync(IPAddress.Loopback, port, ct);
                return;
            }
            catch (SocketException) when (attempt < 40)
            {
                await Task.Delay(50, ct);
            }
        }
    }
}
