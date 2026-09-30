using HiveShock.Zeldathon;

namespace HiveShock.Tests.Zeldathon;

public class ZeldathonClockTests
{
    private sealed class Ticker
    {
        public long Ms;
    }

    private static ZeldathonClockSnapshot Snap(long remaining, ZeldathonRacerStatus status) => new(
        "ralbat", remaining, status,
        new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 10, 7, 13, 1, 0, DateTimeKind.Utc));

    [Fact]
    public void Counts_down_only_while_live()
    {
        var t = new Ticker();
        var clock = new ZeldathonClock(() => t.Ms);
        clock.Update(Snap(14_340_000, ZeldathonRacerStatus.Live));
        t.Ms = 3_000;
        Assert.Equal(14_337_000, clock.RemainingMs());

        clock.Update(Snap(14_337_000, ZeldathonRacerStatus.Paused));
        t.Ms = 60_000;
        Assert.Equal(14_337_000, clock.RemainingMs());
    }

    [Fact]
    public void Never_goes_below_zero_and_new_snapshot_overrides_local_value()
    {
        var t = new Ticker();
        var clock = new ZeldathonClock(() => t.Ms);
        clock.Update(Snap(1_000, ZeldathonRacerStatus.Live));
        t.Ms = 5_000;
        Assert.Equal(0, clock.RemainingMs());
        clock.Update(Snap(9_000, ZeldathonRacerStatus.Live));
        Assert.Equal(9_000, clock.RemainingMs());
    }

    [Fact]
    public void Compensates_half_the_round_trip()
    {
        var t = new Ticker();
        var clock = new ZeldathonClock(() => t.Ms);
        clock.Update(Snap(10_000, ZeldathonRacerStatus.Live), rttMs: 400);
        Assert.Equal(9_800, clock.RemainingMs());
    }

    [Fact]
    public void Freezes_when_the_connection_is_lost_and_resumes_with_the_next_snapshot()
    {
        var t = new Ticker();
        var clock = new ZeldathonClock(() => t.Ms);
        clock.Update(Snap(10_000, ZeldathonRacerStatus.Live));
        t.Ms = 2_000;
        clock.MarkDisconnected();
        t.Ms = 30_000;
        Assert.True(clock.IsDisconnected);
        Assert.Equal(8_000, clock.RemainingMs());

        clock.Update(Snap(7_000, ZeldathonRacerStatus.Live));
        Assert.False(clock.IsDisconnected);
        t.Ms = 31_000;
        Assert.Equal(6_000, clock.RemainingMs());
    }

    [Fact]
    public void Time_until_reset_follows_server_time()
    {
        var t = new Ticker();
        var clock = new ZeldathonClock(() => t.Ms);
        clock.Update(Snap(1, ZeldathonRacerStatus.Online));
        // 13:01 -> 12:00 del día siguiente = 22 h 59 min
        Assert.Equal((22 * 60 + 59) * 60_000L, clock.UntilResetMs());
        t.Ms = 60_000;
        Assert.Equal((22 * 60 + 58) * 60_000L, clock.UntilResetMs());
    }

    [Fact]
    public void Without_data_everything_is_zero()
    {
        var clock = new ZeldathonClock(() => 0);
        Assert.False(clock.HasValue);
        Assert.Equal(0, clock.RemainingMs());
        Assert.Equal(ZeldathonRacerStatus.Unknown, clock.Status);
    }
}
