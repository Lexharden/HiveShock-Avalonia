using HiveShock.Configuration;

namespace HiveShock.Tests.Configuration;

public class FollowConfigTests
{
    [Fact]
    public void OncePerUser_defaults_to_true_and_round_trips_keeping_other_keys()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gifts-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """{ "gifts": [], "follow": { "effect": "impulse", "extra": 5 } }""");
            var editor = new GiftFileEditor(path);
            editor.Load();
            Assert.True(editor.FollowOncePerUser);

            editor.FollowOncePerUser = false;
            editor.Save();

            var again = new GiftFileEditor(path);
            again.Load();
            Assert.False(again.FollowOncePerUser);
            Assert.Equal("impulse", again.FollowEffect);
            Assert.Contains("\"extra\"", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
