using System.Text.Json.Nodes;
using HiveShock.Zeldathon;

namespace HiveShock.Tests.Zeldathon;

public class StreamReporterTests
{
    private sealed class Rig
    {
        public bool Live;
        public long Now;
        public readonly List<JsonNode> Sent = [];
        public readonly StreamReporter Reporter;

        public Rig() => Reporter = new StreamReporter(() => Live, m => Sent.Add(JsonNode.Parse(m.Json)!), () => Now);
    }

    [Fact]
    public void Reports_going_live_once_and_going_off_the_air_at_once()
    {
        var rig = new Rig();
        rig.Reporter.Tick(true);
        Assert.Single(rig.Sent);
        Assert.False(rig.Sent[0]["live"]!.GetValue<bool>());

        rig.Live = true;
        rig.Reporter.SetViewers(120);
        rig.Reporter.Tick(true);
        rig.Reporter.Tick(true);
        Assert.Equal(2, rig.Sent.Count);
        Assert.True(rig.Sent[1]["live"]!.GetValue<bool>());
        Assert.Equal(120, rig.Sent[1]["viewers"]!.GetValue<int>());
        Assert.Equal("STREAM_STATE", rig.Sent[1]["type"]!.GetValue<string>());

        rig.Live = false;
        rig.Reporter.Tick(true);
        Assert.Equal(3, rig.Sent.Count);
        Assert.Null(rig.Sent[2]["viewers"]);
    }

    [Fact]
    public void Viewer_changes_are_throttled()
    {
        var rig = new Rig { Live = true };
        rig.Reporter.SetViewers(10);
        rig.Reporter.Tick(true);
        rig.Reporter.SetViewers(11);
        rig.Now += 5_000;
        rig.Reporter.Tick(true);
        Assert.Single(rig.Sent);

        rig.Now += 26_000;
        rig.Reporter.Tick(true);
        Assert.Equal(2, rig.Sent.Count);
        Assert.Equal(11, rig.Sent[1]["viewers"]!.GetValue<int>());
    }

    [Fact]
    public void Nothing_is_sent_while_disconnected_and_everything_is_resent_after_reconnecting()
    {
        var rig = new Rig { Live = true };
        rig.Reporter.SetViewers(5);
        rig.Reporter.Tick(false);
        Assert.Empty(rig.Sent);

        rig.Reporter.Tick(true);
        Assert.Single(rig.Sent);
        rig.Reporter.Forget();
        rig.Reporter.Tick(true);
        Assert.Equal(2, rig.Sent.Count);
    }

    [Fact]
    public void Viewers_are_hidden_when_not_live_and_negative_counts_are_clamped()
    {
        var rig = new Rig();
        rig.Reporter.SetViewers(300);
        rig.Reporter.Tick(true);
        Assert.Null(rig.Sent[0]["viewers"]);

        rig.Live = true;
        rig.Reporter.SetViewers(-4);
        rig.Reporter.Tick(true);
        Assert.Equal(0, rig.Sent[1]["viewers"]!.GetValue<int>());
    }
}
