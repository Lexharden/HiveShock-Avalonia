using HiveShock.Zeldathon;

namespace HiveShock.Tests.Zeldathon;

public class TimerDisplayTests
{
    private sealed class Ticker
    {
        public long Ms;
    }

    private static ZeldathonClockSnapshot Snap(long remaining, ZeldathonRacerStatus status) => new(
        "ralbat", remaining, status,
        new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 10, 8, 11, 0, 0, DateTimeKind.Utc));

    private static (ZeldathonClock Clock, Ticker T) Clock(long remaining, ZeldathonRacerStatus status)
    {
        var t = new Ticker();
        var clock = new ZeldathonClock(() => t.Ms);
        clock.Update(Snap(remaining, status));
        return (clock, t);
    }

    [Theory]
    [InlineData(0, "hms", true, "00:00:00")]
    [InlineData(14_340_000, "hms", true, "03:59:00")]
    [InlineData(14_339_001, "hms", true, "03:59:00")]
    [InlineData(14_339_001, "hms", false, "03:58:59")]
    [InlineData(14_340_000, "ms", true, "239:00")]
    [InlineData(-5, "hms", true, "00:00:00")]
    public void Formats_time(long ms, string format, bool up, string expected) =>
        Assert.Equal(expected, TimerDisplayBuilder.FormatTime(ms, format, up));

    [Fact]
    public void Official_live_clock_is_normal_then_warns_then_turns_critical()
    {
        var opt = new TimerDisplayOptions { WarnMinutes = 30, CriticalMinutes = 5 };
        var (clock, t) = Clock(3 * 3600_000L, ZeldathonRacerStatus.Live);
        var shown = TimerDisplayBuilder.Official(clock, opt, ZeldathonConnectionState.Connected);
        Assert.Equal("EN VIVO", shown.StatusText);
        Assert.Equal(TimerTone.Normal, shown.Tone);
        Assert.Equal("03:00:00", shown.TimeText);

        Assert.Equal(TimerTone.Warn, TimerDisplayBuilder.Official(Clock(29 * 60_000L, ZeldathonRacerStatus.Live).Clock, opt, ZeldathonConnectionState.Connected).Tone);
        Assert.Equal(TimerTone.Critical, TimerDisplayBuilder.Official(Clock(4 * 60_000L, ZeldathonRacerStatus.Live).Clock, opt, ZeldathonConnectionState.Connected).Tone);

        t.Ms = 3 * 3600_000L;
        Assert.Equal(TimerTone.Exhausted, TimerDisplayBuilder.Official(clock, opt, ZeldathonConnectionState.Connected).Tone);
    }

    [Fact]
    public void Official_states_map_to_labels()
    {
        var opt = new TimerDisplayOptions();
        string Status(ZeldathonRacerStatus s) =>
            TimerDisplayBuilder.Official(Clock(3600_000, s).Clock, opt, ZeldathonConnectionState.Connected).StatusText;
        Assert.Equal("PAUSADO", Status(ZeldathonRacerStatus.Paused));
        Assert.Equal("AGOTADO", Status(ZeldathonRacerStatus.Exhausted));
        Assert.Equal("EN ESPERA", Status(ZeldathonRacerStatus.Online));
        Assert.Equal("TERMINÓ", Status(ZeldathonRacerStatus.Finished));
    }

    [Fact]
    public void Official_shows_used_fraction_and_reset_countdown()
    {
        var opt = new TimerDisplayOptions { BudgetSeconds = 4 * 3600 };
        var shown = TimerDisplayBuilder.Official(Clock(3 * 3600_000L, ZeldathonRacerStatus.Paused).Clock, opt, ZeldathonConnectionState.Connected);
        Assert.Equal(0.25, shown.UsedFraction, 3);
        Assert.Equal("Reinicia en 01:00:00", shown.ResetText);
    }

    [Fact]
    public void Official_without_data_or_connection_is_offline_and_explains_why()
    {
        var opt = new TimerDisplayOptions();
        var empty = new ZeldathonClock(() => 0);
        Assert.Equal("SIN CONEXIÓN", TimerDisplayBuilder.Official(empty, opt, ZeldathonConnectionState.Reconnecting).StatusText);
        Assert.Equal("TOKEN NO VÁLIDO", TimerDisplayBuilder.Official(empty, opt, ZeldathonConnectionState.AuthFailed).StatusText);
        Assert.Equal("ESPERANDO AL SERVIDOR", TimerDisplayBuilder.Official(empty, opt, ZeldathonConnectionState.Connected).StatusText);
        Assert.Equal("--:--", TimerDisplayBuilder.Official(empty, new TimerDisplayOptions { Format = "ms" }, ZeldathonConnectionState.Stopped).TimeText);
    }

    [Fact]
    public void Official_after_a_dropped_connection_is_frozen_and_flagged()
    {
        var (clock, t) = Clock(3600_000, ZeldathonRacerStatus.Live);
        t.Ms = 10_000;
        clock.MarkDisconnected();
        t.Ms = 600_000;
        var shown = TimerDisplayBuilder.Official(clock, new TimerDisplayOptions(), ZeldathonConnectionState.Reconnecting);
        Assert.Equal(TimerTone.Offline, shown.Tone);
        Assert.Equal("00:59:50", shown.TimeText);
        Assert.Contains("DETENIDO", shown.StatusText);
    }

    [Fact]
    public void Local_stopwatch_counts_up_and_down()
    {
        var t = new Ticker();
        var sw = new LocalStopwatch(0, () => t.Ms);
        var up = new TimerDisplayOptions();
        Assert.Equal("PAUSADO", TimerDisplayBuilder.Local(sw, up).StatusText);
        sw.Start();
        t.Ms = 65_000;
        var shown = TimerDisplayBuilder.Local(sw, up);
        Assert.Equal("00:01:05", shown.TimeText);
        Assert.Equal("CORRIENDO", shown.StatusText);

        var down = new TimerDisplayOptions { LocalCountdown = true, LocalStartMinutes = 2 };
        Assert.Equal("00:00:55", TimerDisplayBuilder.Local(sw, down).TimeText);
        t.Ms = 200_000;
        var done = TimerDisplayBuilder.Local(sw, down);
        Assert.Equal("TIEMPO", done.StatusText);
        Assert.Equal(TimerTone.Exhausted, done.Tone);
    }

    [Fact]
    public void Stopwatch_pause_reset_and_restore()
    {
        var t = new Ticker();
        var sw = new LocalStopwatch(5_000, () => t.Ms);
        Assert.Equal(5_000, sw.ElapsedMs);
        sw.Start();
        t.Ms = 3_000;
        sw.Pause();
        t.Ms = 90_000;
        Assert.Equal(8_000, sw.ElapsedMs);
        Assert.False(sw.IsRunning);
        sw.Reset();
        Assert.Equal(0, sw.ElapsedMs);
    }

    [Fact]
    public void Local_stopwatch_counting_up_shows_what_donations_added_or_removed()
    {
        var t = new Ticker();
        var sw = new LocalStopwatch(10_000, () => t.Ms);
        var up = new TimerDisplayOptions();

        sw.Adjust(30_000);
        Assert.Equal("00:00:40", TimerDisplayBuilder.Local(sw, up).TimeText);

        sw.Adjust(-100_000); // no baja de cero
        Assert.Equal("00:00:00", TimerDisplayBuilder.Local(sw, up).TimeText);
        Assert.Equal(0, TimerDisplayBuilder.LocalValueMs(sw, up));
    }

    [Fact]
    public void Local_countdown_shows_the_remaining_time_with_the_donation_adjustment()
    {
        var t = new Ticker();
        var sw = new LocalStopwatch(0, () => t.Ms);
        var down = new TimerDisplayOptions { LocalCountdown = true, LocalStartMinutes = 2 };
        sw.Start();
        t.Ms = 5_000;

        Assert.Equal("00:01:55", TimerDisplayBuilder.Local(sw, down).TimeText);
        sw.Adjust(60_000); // suma 1 min: queda más tiempo
        Assert.Equal("00:02:55", TimerDisplayBuilder.Local(sw, down).TimeText);
        sw.Adjust(-120_000); // resta 2 min
        Assert.Equal("00:00:55", TimerDisplayBuilder.Local(sw, down).TimeText);
        Assert.Equal(55_000, TimerDisplayBuilder.LocalValueMs(sw, down));

        t.Ms = 70_000; // sigue corriendo: el ajuste no se pierde
        Assert.Equal("TIEMPO", TimerDisplayBuilder.Local(sw, down).StatusText);
        Assert.Equal(0, TimerDisplayBuilder.LocalValueMs(sw, down));
    }

    [Fact]
    public void Local_countdown_bar_follows_the_adjusted_remaining_time()
    {
        var t = new Ticker();
        var sw = new LocalStopwatch(0, () => t.Ms);
        var down = new TimerDisplayOptions { LocalCountdown = true, LocalStartMinutes = 2 };

        Assert.Equal(0, TimerDisplayBuilder.Local(sw, down).UsedFraction, 3);
        sw.Adjust(-60_000);
        Assert.Equal(0.5, TimerDisplayBuilder.Local(sw, down).UsedFraction, 3);
        sw.Adjust(120_000); // más tiempo que al empezar: la barra no pasa de vacía
        Assert.Equal(0, TimerDisplayBuilder.Local(sw, down).UsedFraction, 3);
    }

    [Fact]
    public void Stopwatch_adjustment_works_paused_or_running_and_reset_clears_it()
    {
        var t = new Ticker();
        var sw = new LocalStopwatch(0, () => t.Ms);

        sw.Adjust(5_000); // en pausa
        sw.Start();
        t.Ms = 2_000;
        sw.Adjust(1_000); // corriendo
        Assert.Equal(6_000, sw.OffsetMs);
        Assert.Equal(2_000, sw.ElapsedMs); // el tiempo transcurrido no se toca

        sw.Pause();
        sw.Reset();
        Assert.Equal(0, sw.OffsetMs);
        Assert.Equal(0, sw.ElapsedMs);
    }

    [Fact]
    public void Stopwatch_restores_its_saved_offset()
    {
        var sw = new LocalStopwatch(4_000, () => 0, offsetMs: -1_500);

        Assert.Equal(4_000, sw.ElapsedMs);
        Assert.Equal(-1_500, sw.OffsetMs);
    }

    [Fact]
    public void Stopwatch_adjustments_from_many_threads_are_all_counted()
    {
        var sw = new LocalStopwatch();
        Parallel.For(0, 1000, _ => sw.Adjust(10));

        Assert.Equal(10_000, sw.OffsetMs);
    }
}
