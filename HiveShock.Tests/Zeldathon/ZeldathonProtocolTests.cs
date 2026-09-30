using HiveShock.Zeldathon;

namespace HiveShock.Tests.Zeldathon;

public class ZeldathonProtocolTests
{
    // Mismo formato que contract/rest/clock.json del backend.
    private const string ClockJson = """
        {"type":"CLOCK","id":"hb-1","clock":{"racerId":"ralbat","remainingMs":14340000,
        "resetAtUtc":"2026-10-08T12:00:00.000Z","serverTimeUtc":"2026-10-07T13:01:00.000Z","status":"live"}}
        """;

    [Fact]
    public void Parses_a_clock_from_the_server()
    {
        var msg = ZeldathonProtocol.Parse(ClockJson);
        Assert.Equal(ZeldathonInboundKind.Clock, msg.Kind);
        Assert.Equal("hb-1", msg.Id);
        Assert.Equal(14_340_000, msg.Clock!.RemainingMs);
        Assert.Equal(ZeldathonRacerStatus.Live, msg.Clock.Status);
        Assert.Equal(new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc), msg.Clock.ResetAtUtc);
        Assert.Equal("ralbat", msg.Clock.RacerId);
    }

    [Fact]
    public void Parses_ack_error_and_force_close_and_ignores_garbage()
    {
        Assert.Equal(ZeldathonInboundKind.Ack, ZeldathonProtocol.Parse("""{"type":"ACK","id":"a"}""").Kind);
        Assert.Equal(ZeldathonInboundKind.ForceClose, ZeldathonProtocol.Parse("""{"type":"GAME_FORCE_CLOSE"}""").Kind);
        var err = ZeldathonProtocol.Parse("""{"type":"ERROR","id":"a","code":"exhausted","message":"x"}""");
        Assert.Equal(ZeldathonInboundKind.Error, err.Kind);
        Assert.Equal("exhausted", err.Code);
        Assert.Equal(ZeldathonInboundKind.Unknown, ZeldathonProtocol.Parse("no es json").Kind);
        Assert.Equal(ZeldathonInboundKind.Unknown, ZeldathonProtocol.Parse("[1]").Kind);
        Assert.Equal(ZeldathonInboundKind.Unknown, ZeldathonProtocol.Parse("""{"type":"CLOCK"}""").Kind);
    }

    [Fact]
    public void Outbound_messages_carry_type_and_a_unique_id()
    {
        var a = ZeldathonProtocol.Message("ITEM_ACQUIRED", fields: new System.Text.Json.Nodes.JsonObject { ["item"] = "hookshot" });
        var b = ZeldathonProtocol.Message("ITEM_ACQUIRED", fields: new System.Text.Json.Nodes.JsonObject { ["item"] = "hookshot" });
        Assert.NotEqual(a.Id, b.Id);
        Assert.Contains("\"type\":\"ITEM_ACQUIRED\"", a.Json);
        Assert.Contains("\"item\":\"hookshot\"", a.Json);
        Assert.Contains($"\"id\":\"{a.Id}\"", a.Json);
        Assert.Contains("\"gameRunning\":true", ZeldathonProtocol.Heartbeat("hb-1", true).Json);
        Assert.DoesNotContain("gameRunning", ZeldathonProtocol.Heartbeat("hb-2", null).Json);
    }

    [Fact]
    public void Explains_known_codes_in_plain_language()
    {
        Assert.Contains("todavía no", ZeldathonProtocol.Explain("event_not_live", ""));
        Assert.Equal("boom", ZeldathonProtocol.Explain("weird", "boom"));
    }
}
