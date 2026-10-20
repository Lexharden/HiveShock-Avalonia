using HiveShock.Configuration;
using HiveShock.Input;

namespace HiveShock.Tests.Configuration;

public class CustomProfileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"hs-prof-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Theory]
    [InlineData("Mi stream (Minecraft)", "mi-stream-minecraft")]
    [InlineData("  Pruebas  ", "pruebas")]
    [InlineData("Ñandú rápido", "nandu-rapido")]
    [InlineData("!!!", "perfil")]
    public void MakeId_builds_a_safe_slug(string name, string expected) =>
        Assert.Equal(expected, CustomProfileStore.MakeId(name, []));

    [Fact]
    public void MakeId_avoids_taken_ids_and_existing_folders()
    {
        Directory.CreateDirectory(Path.Combine(_root, "pruebas-2"));
        Assert.Equal("pruebas-3", CustomProfileStore.MakeId("Pruebas", ["pruebas"], _root));
    }

    [Fact]
    public void Blank_profile_loads_with_no_game_effects_and_chat_on()
    {
        var dir = CustomProfileStore.Create(_root, [], "Mi OBS", ProfileTemplate.Blank);
        var profile = ProfileStore.LoadFromDirectory(dir);

        Assert.Equal("mi-obs", profile.Id);
        Assert.Equal("Mi OBS", profile.DisplayName);

        var macros = new KeyMacroCatalog([new KeyMacro { Id = "teclas_1", Steps = [new KeyStep { Key = "Space" }] }]);
        var effects = EffectCatalog.Load(profile.EffectsPath, profile.Info, macros);
        Assert.Equal(["teclas_1"], effects.Ids.ToArray());

        var editor = new GiftFileEditor(profile.GiftsPath);
        editor.Load();
        Assert.True(editor.ChatEnabled);
        Assert.Empty(editor.ChatCommands);
        Assert.Empty(editor.Gifts);
    }

    [Fact]
    public void Starter_profile_links_the_example_command_to_the_macro()
    {
        var dir = CustomProfileStore.Create(_root, [], "Con ejemplo", ProfileTemplate.Starter, starterMacroId: "teclas_1");
        var editor = new GiftFileEditor(Path.Combine(dir, "gifts.json"));
        editor.Load();

        var cmd = Assert.Single(editor.ChatCommands);
        Assert.Equal(CustomProfileStore.StarterWord, cmd.Word);
        Assert.Equal("teclas_1", cmd.Effect);
    }

    [Fact]
    public void Copy_keeps_effects_and_commands_but_gets_its_own_identity()
    {
        var sourceDir = CustomProfileStore.Create(_root, [], "Origen", ProfileTemplate.Starter, starterMacroId: "teclas_1");
        var source = ProfileStore.LoadFromDirectory(sourceDir);

        var copyDir = CustomProfileStore.Create(_root, ["origen"], "Origen", ProfileTemplate.CopyOf, source);
        var copy = ProfileStore.LoadFromDirectory(copyDir);

        Assert.Equal("origen-2", copy.Id);
        var editor = new GiftFileEditor(copy.GiftsPath);
        editor.Load();
        Assert.Equal("teclas_1", Assert.Single(editor.ChatCommands).Effect);
    }

    [Fact]
    public void Rejects_empty_and_overlong_names()
    {
        Assert.Throws<ArgumentException>(() => CustomProfileStore.Create(_root, [], "   ", ProfileTemplate.Blank));
        Assert.Throws<ArgumentException>(() =>
            CustomProfileStore.Create(_root, [], new string('x', CustomProfileStore.MaxNameLength + 1), ProfileTemplate.Blank));
    }
}

public class ChatPlatformsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"hs-chat-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private GiftConfigStore Load(LoadedGameProfile profile)
    {
        var macros = new KeyMacroCatalog([new KeyMacro { Id = "teclas_1", Steps = [new KeyStep { Key = "Space" }] }]);
        var store = new GiftConfigStore(EffectCatalog.Load(profile.EffectsPath, profile.Info, macros));
        store.Reload(profile.GiftsPath);
        return store;
    }

    [Fact]
    public void New_profile_command_answers_on_tiktok_and_twitch()
    {
        var dir = CustomProfileStore.Create(_root, [], "Dos canales", ProfileTemplate.Starter, starterMacroId: "teclas_1");
        var snap = Load(ProfileStore.LoadFromDirectory(dir)).Snapshot;

        Assert.True(snap.Chat.Enabled);
        Assert.True(snap.TwitchChat.Enabled);
        Assert.Equal("teclas_1", snap.Chat.Commands["saltar"]);
        Assert.Equal("teclas_1", snap.TwitchChat.Commands["saltar"]);
    }

    [Fact]
    public void After_saving_each_platform_keeps_its_own_commands()
    {
        var dir = CustomProfileStore.Create(_root, [], "Separados", ProfileTemplate.Starter, starterMacroId: "teclas_1");
        var profile = ProfileStore.LoadFromDirectory(dir);

        var editor = new GiftFileEditor(profile.GiftsPath);
        editor.Load();
        editor.ChatCommands.Add(new EditableChatCommand { Word = "solotiktok", Effect = "teclas_1" });
        editor.Save();

        var snap = Load(profile).Snapshot;
        Assert.True(snap.Chat.Commands.ContainsKey("solotiktok"));
        Assert.False(snap.TwitchChat.Commands.ContainsKey("solotiktok"));
        Assert.True(snap.TwitchChat.Commands.ContainsKey("saltar"));
    }
}

public class CommandWithoutEffectTests
{
    [Fact]
    public void Command_with_no_effect_survives_save_and_reload()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gifts-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """{ "gifts": [] }""");
            var editor = new GiftFileEditor(path);
            editor.Load();
            editor.ChatCommands.Add(new EditableChatCommand { Word = "salto", Effect = "" });
            editor.TwitchChatCommands.Add(new EditableChatCommand { Word = "salto", Effect = "" });
            editor.Save();

            var again = new GiftFileEditor(path);
            again.Load();
            Assert.Equal("salto", Assert.Single(again.ChatCommands).Word);
            Assert.Equal("salto", Assert.Single(again.TwitchChatCommands).Word);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".bak");
        }
    }
}
