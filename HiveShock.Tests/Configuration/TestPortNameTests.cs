using HiveShock.Configuration;

namespace HiveShock.Tests.Configuration;

public class TestPortNameTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"hs-port-{Guid.NewGuid():N}");

    public TestPortNameTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Write(string json)
    {
        var path = Path.Combine(_dir, "profile.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Reads_the_new_key()
    {
        var info = ProfileInfoEditor.ImportFrom(Write("""{ "id": "a", "displayName": "A", "testPort": 5555 }"""));
        Assert.Equal(5555, info.TestPort);
    }

    [Fact]
    public void Still_reads_the_old_crowdControlPort_key()
    {
        var info = ProfileInfoEditor.ImportFrom(Write("""{ "id": "a", "displayName": "A", "crowdControlPort": 4444 }"""));
        Assert.Equal(4444, info.TestPort);
    }

    [Fact]
    public void Saving_writes_only_the_new_key()
    {
        var path = Write("""{ "id": "a", "displayName": "A", "crowdControlPort": 4444 }""");
        var editor = new ProfileInfoEditor(path);
        editor.Load();
        editor.Save();

        var text = File.ReadAllText(path);
        Assert.Contains("\"testPort\": 4444", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("crowdControlPort", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Env_falls_back_to_the_old_names()
    {
        const string oldName = "HS_TEST_OLD_NAME";
        const string newName = "HS_TEST_NEW_NAME";
        try
        {
            Environment.SetEnvironmentVariable(oldName, "1");
            Assert.Equal("1", BridgeOptions.EnvAny(newName, oldName));

            Environment.SetEnvironmentVariable(newName, "2");
            Assert.Equal("2", BridgeOptions.EnvAny(newName, oldName));
        }
        finally
        {
            Environment.SetEnvironmentVariable(oldName, null);
            Environment.SetEnvironmentVariable(newName, null);
        }
    }
}
