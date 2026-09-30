using HiveShock.Zeldathon;

namespace HiveShock.Tests.Zeldathon;

public class ZeldathonSettingsTests
{
    private static string? Url(string server)
    {
        var s = new ZeldathonSettings { ServerUrl = server };
        return s.TryBuildIngestUri(out var uri, out _) ? uri.ToString() : null;
    }

    [Theory]
    [InlineData("zeldathon.example.com", "wss://zeldathon.example.com/ingest")]
    [InlineData("https://zeldathon.example.com/", "wss://zeldathon.example.com/ingest")]
    [InlineData("wss://zeldathon.example.com/ingest", "wss://zeldathon.example.com/ingest")]
    [InlineData("https://example.com/zelda?x=1#f", "wss://example.com/zelda/ingest")]
    [InlineData("http://localhost:8080", "ws://localhost:8080/ingest")]
    [InlineData("ws://127.0.0.1:8080/", "ws://127.0.0.1:8080/ingest")]
    public void Builds_the_ingest_address(string input, string expected) =>
        Assert.Equal(expected, Url(input));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("http://zeldathon.example.com")]
    [InlineData("ws://zeldathon.example.com")]
    [InlineData("ftp://zeldathon.example.com")]
    public void Rejects_empty_or_unencrypted_remote_servers(string input) => Assert.Null(Url(input));

    [Fact]
    public void Token_is_never_shown_in_full()
    {
        Assert.Equal("", ZeldathonSettings.Mask(null));
        Assert.Equal("abcd••••wxyz", ZeldathonSettings.Mask("abcdEFGHIJKLMNOPwxyz"));
        Assert.DoesNotContain("EFGH", ZeldathonSettings.Mask("abcdEFGHIJKLMNOPwxyz"));
        Assert.Equal("•••", ZeldathonSettings.Mask("abc"));
    }
}
