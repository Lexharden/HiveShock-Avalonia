using HiveShock.Live;
using TikTokLive.Proto;

namespace HiveShock.Tests.Live;

public class ViewerIdentityTests
{
    [Fact]
    public void TikTok_uses_the_numeric_id_and_the_lowercase_unique_id()
    {
        var viewer = ViewerIdentity.TikTok(new UserIdentity { UserId = 7001, UniqueId = "@Ana_99", Nickname = "Ana ✨" });

        Assert.Equal(LivePortIds.TikTok, viewer.PortId);
        Assert.Equal("7001", viewer.UserId);
        Assert.Equal("ana_99", viewer.Handle);
        Assert.Equal("Ana ✨", viewer.Name);
        Assert.Equal("7001", viewer.StableKey);
        Assert.True(viewer.HasId);
    }

    [Fact]
    public void TikTok_without_numeric_id_falls_back_to_the_handle_never_to_the_nickname()
    {
        var viewer = ViewerIdentity.TikTok(new UserIdentity { UserId = 0, UniqueId = "ana", Nickname = "Maria" });

        Assert.False(viewer.HasId);
        Assert.True(viewer.IsIdentified);
        Assert.Equal("ana", viewer.StableKey);
    }

    [Fact]
    public void A_nickname_alone_does_not_identify_anyone()
    {
        var viewer = ViewerIdentity.TikTok(new UserIdentity { UserId = 0, UniqueId = "", Nickname = "Maria" });

        Assert.False(viewer.IsIdentified);
        Assert.Equal("Maria", viewer.Label);
    }

    [Fact]
    public void Null_user_is_anonymous()
    {
        var viewer = ViewerIdentity.TikTok(null);

        Assert.False(viewer.IsIdentified);
        Assert.Equal("anónimo", viewer.Label);
    }

    [Fact]
    public void Twitch_normalises_login_and_keeps_the_display_name()
    {
        var viewer = ViewerIdentity.Twitch(" 123 ", "@Fan_Del_Stream", "Fan Del Stream");

        Assert.Equal(LivePortIds.Twitch, viewer.PortId);
        Assert.Equal("123", viewer.UserId);
        Assert.Equal("fan_del_stream", viewer.Handle);
        Assert.Equal("Fan Del Stream", viewer.Name);
    }

    [Fact]
    public void Detail_shows_name_handle_and_id_without_repeating_the_name()
    {
        var named = ViewerIdentity.TikTok(new UserIdentity { UserId = 5, UniqueId = "ana", Nickname = "Ana María" });
        var same = ViewerIdentity.TikTok(new UserIdentity { UserId = 5, UniqueId = "ana", Nickname = "Ana" });

        Assert.Equal("Ana María (@ana · id 5)", named.Detail);
        Assert.Equal("@ana · id 5", same.Detail);
    }
}
