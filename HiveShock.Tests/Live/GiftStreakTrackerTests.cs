using TikTokLive.Helpers;
using TikTokLive.Proto;

namespace HiveShock.Tests.Live;

public class GiftStreakTrackerTests
{
    private static WebcastGiftMessage Gift(ulong group, int repeat, long msgId, bool end = false, int diamonds = 1) => new()
    {
        GroupId = group,
        GiftId = 5,
        RepeatCount = repeat,
        RepeatEnd = end ? 1 : 0,
        Common = new CommonMessageData { MsgId = msgId },
        GiftDetails = new GiftDetails { GiftType = 1, DiamondCount = diamonds, GiftName = "Mishka Bear" },
        User = new UserIdentity { Nickname = "Ana" },
    };

    [Fact]
    public void Restarted_count_in_the_same_combo_is_accumulated()
    {
        // TikTok reutiliza el GroupId para un segundo envío y vuelve a contar desde x1.
        var tracker = new GiftStreakTracker();

        var first = tracker.Process(Gift(7, 1, msgId: 100));
        var second = tracker.Process(Gift(7, 1, msgId: 101));
        var final = tracker.Process(Gift(7, 1, msgId: 102, end: true));

        Assert.False(first.Restarted);
        Assert.True(second.Restarted);
        Assert.Equal(2, second.TotalGiftCount);
        Assert.True(final.IsFinal);
        Assert.Equal(2, final.TotalGiftCount); // antes se perdía el primer regalo y salía x1
        Assert.Equal(0, tracker.ActiveStreaks());
    }

    [Fact]
    public void Repeated_message_is_not_counted_twice()
    {
        var tracker = new GiftStreakTracker();
        tracker.Process(Gift(8, 1, msgId: 200));

        var duplicate = tracker.Process(Gift(8, 1, msgId: 200));

        Assert.False(duplicate.Restarted);
        Assert.Equal(1, duplicate.TotalGiftCount);
    }

    [Fact]
    public void Normal_combo_counts_the_running_total()
    {
        var tracker = new GiftStreakTracker();
        tracker.Process(Gift(9, 1, 300));
        tracker.Process(Gift(9, 2, 301));
        var third = tracker.Process(Gift(9, 3, 302));
        var final = tracker.Process(Gift(9, 3, 303, end: true));

        Assert.Equal(1, third.EventGiftCount);
        Assert.False(third.Restarted);
        Assert.Equal(3, final.TotalGiftCount);
    }

    [Fact]
    public void Combo_without_closing_message_is_flushed_with_the_accumulated_total()
    {
        var tracker = new GiftStreakTracker();
        tracker.Process(Gift(10, 1, 400, diamonds: 1000));
        tracker.Process(Gift(10, 1, 401, diamonds: 1000)); // reinicio: x1 + x1

        Thread.Sleep(GiftStreakTracker.PremiumComboIdleTimeout + TimeSpan.FromMilliseconds(200));
        var flushed = Assert.Single(tracker.FlushStale());

        Assert.Equal(2, flushed.TotalGiftCount);
        Assert.Equal(2000, flushed.TotalDiamondCount);
        Assert.Equal("Ana", flushed.ViewerName);
    }
}
