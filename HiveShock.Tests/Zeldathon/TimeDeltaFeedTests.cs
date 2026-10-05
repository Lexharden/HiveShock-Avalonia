using HiveShock.Zeldathon;

namespace HiveShock.Tests.Zeldathon;

public class TimeDeltaFeedTests
{
    private long _ms;
    private DateTime _utc = new(2026, 10, 7, 13, 0, 0, DateTimeKind.Utc);

    private TimeDeltaFeed Feed() => new(() => _ms, () => _utc);

    private static DonationTimeApplied Applied(long seconds, string limitedBy = "", DateTime? created = null) =>
        new(seconds, seconds, limitedBy, created ?? new DateTime(2026, 10, 7, 13, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void Nothing_is_shown_until_something_is_pushed()
    {
        Assert.Null(Feed().Current());
    }

    [Fact]
    public void A_single_donation_is_shown_with_its_sign_and_age_then_expires()
    {
        var feed = Feed();
        feed.Push(-30, limited: false);

        _ms = 400;
        var burst = feed.Current()!;
        Assert.Equal(-30, burst.Seconds);
        Assert.Equal(1, burst.Count);
        Assert.Equal(400, burst.AgeMs);
        Assert.False(burst.Limited);

        _ms = (long)TimeDeltaFeed.HoldFor.TotalMilliseconds;
        Assert.Null(feed.Current());
    }

    [Fact]
    public void Donations_close_together_merge_into_one_net_change()
    {
        var feed = Feed();
        feed.Push(60, false);
        _ms = 1000;
        feed.Push(60, false);
        _ms = 2200; // 1,2 s después de la anterior: sigue siendo la misma ráfaga
        feed.Push(-30, false);

        var burst = feed.Current()!;
        Assert.Equal(90, burst.Seconds);
        Assert.Equal(3, burst.Count);
        Assert.Equal(0, burst.AgeMs); // cada donación reinicia la animación
    }

    [Fact]
    public void A_donation_after_the_merge_window_starts_a_fresh_burst()
    {
        var feed = Feed();
        feed.Push(60, false);
        _ms = 2000; // más de 1,5 s después
        feed.Push(-10, false);

        var burst = feed.Current()!;
        Assert.Equal(-10, burst.Seconds);
        Assert.Equal(1, burst.Count);
    }

    [Fact]
    public void Version_grows_with_every_push_so_the_screen_knows_to_redraw()
    {
        var feed = Feed();
        feed.Push(10, false);
        var first = feed.Current()!.Version;
        feed.Push(10, false);

        Assert.True(feed.Current()!.Version > first);
    }

    [Fact]
    public void A_limited_donation_marks_the_whole_burst()
    {
        var feed = Feed();
        feed.Push(60, false);
        _ms = 200;
        feed.Push(60, limited: true);

        Assert.True(feed.Current()!.Limited);
    }

    [Fact]
    public void Opposite_changes_that_cancel_out_show_zero_and_zero_alone_is_ignored()
    {
        var feed = Feed();
        feed.Push(0, false);
        Assert.Null(feed.Current());

        feed.Push(60, false);
        _ms = 100;
        feed.Push(-60, false);
        Assert.Equal(0, feed.Current()!.Seconds); // la pantalla no pinta nada con 0
    }

    [Fact]
    public void Pushed_is_raised_for_every_change_but_not_for_zero()
    {
        var feed = Feed();
        var raised = 0;
        feed.Pushed += () => raised++;

        feed.Push(0, false);
        feed.Push(5, false);
        feed.Push(-5, false);

        Assert.Equal(2, raised);
    }

    [Fact]
    public void A_confirmation_that_arrives_too_late_is_not_news()
    {
        var feed = Feed();
        feed.Add(Applied(60, created: _utc - TimeDeltaFeed.MaxConfirmationAge - TimeSpan.FromSeconds(1)));
        Assert.Null(feed.Current());

        feed.Add(Applied(60, created: _utc - TimeSpan.FromSeconds(10)));
        Assert.Equal(60, feed.Current()!.Seconds);
    }

    [Fact]
    public void A_limited_confirmation_is_flagged()
    {
        var feed = Feed();
        feed.Add(Applied(30, limitedBy: "per_donation"));

        Assert.True(feed.Current()!.Limited);
    }

    [Fact]
    public void Concurrent_pushes_are_all_counted()
    {
        var feed = Feed();
        Parallel.For(0, 500, _ => feed.Push(1, false));

        var burst = feed.Current()!;
        Assert.Equal(500, burst.Seconds);
        Assert.Equal(500, burst.Count);
    }

    [Theory]
    [InlineData(90, "+1:30")]
    [InlineData(-30, "−0:30")]
    [InlineData(60, "+1:00")]
    [InlineData(-725, "−12:05")]
    [InlineData(3905, "+1:05:05")]
    [InlineData(-3600, "−1:00:00")]
    public void Formats_the_change_with_a_sign(long seconds, string expected) =>
        Assert.Equal(expected, TimerDisplayBuilder.FormatDelta(seconds));
}
