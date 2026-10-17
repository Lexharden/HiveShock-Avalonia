using HiveShock.Configuration;
using HiveShock.Input;

namespace HiveShock.Tests.Configuration;

public class SaveRobustnessTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"hs-save-{Guid.NewGuid():N}");

    public SaveRobustnessTests() => Directory.CreateDirectory(_dir);

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

    [Fact]
    public void Gifts_save_is_atomic_and_keeps_previous_copy_as_bak()
    {
        var path = Path.Combine(_dir, "gifts.json");
        File.WriteAllText(path, """{ "gifts": [] }""");
        var editor = new GiftFileEditor(path);
        editor.Load();
        editor.ChatCommands.Add(new EditableChatCommand { Word = "salto", Effect = "impulse" });
        editor.Save();

        Assert.True(File.Exists(path + ".bak"));
        Assert.False(File.Exists(path + ".tmp"));
        var again = new GiftFileEditor(path);
        again.Load();
        Assert.Single(again.ChatCommands);
        Assert.Equal("salto", again.ChatCommands[0].Word);
    }

    [Fact]
    public void Gifts_load_recovers_from_bak_when_file_is_truncated()
    {
        var path = Path.Combine(_dir, "gifts.json");
        File.WriteAllText(path, """{ "gifts": [] }""");
        var editor = new GiftFileEditor(path);
        editor.Load();
        editor.ChatCommands.Add(new EditableChatCommand { Word = "salto", Effect = "impulse" });
        editor.Save();
        editor.Save(); // el .bak ahora contiene el comando

        File.WriteAllText(path, """{ "gifts": [ { "gift": "Ro"""); // cierre a mitad de escritura

        var recovered = new GiftFileEditor(path);
        recovered.Load();
        Assert.Single(recovered.ChatCommands);
        Assert.True(File.Exists(path + ".corrupt"));
    }

    [Fact]
    public void Macros_load_recovers_from_bak_when_file_is_empty()
    {
        var path = Path.Combine(_dir, "keys.json");
        var macro = new KeyMacro { Id = "teclas_1", Label = "Saltar", Steps = [new KeyStep { Key = "Space" }] };
        KeyMacroCatalogStore.Save(path, [macro]);
        KeyMacroCatalogStore.Save(path, [macro]);

        File.WriteAllText(path, "");

        var catalog = KeyMacroCatalogStore.Load(path);
        Assert.True(catalog.Contains("teclas_1"));
    }
}

public class MacroLibraryTests : IDisposable
{
    private readonly string _a = Path.Combine(Path.GetTempPath(), $"hs-lib-a-{Guid.NewGuid():N}");
    private readonly string _b = Path.Combine(Path.GetTempPath(), $"hs-lib-b-{Guid.NewGuid():N}");

    public void Dispose()
    {
        foreach (var d in new[] { _a, _b })
        {
            try
            {
                Directory.Delete(d, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static MacroLibrary Sample() => new()
    {
        Window = new WindowTarget { ProcessName = "Game" },
        Macros = [new KeyMacro { Id = "teclas_1", Label = "Saltar", Steps = [new KeyStep { Key = "Space" }] }],
    };

    [Fact]
    public void Save_then_load_round_trips_macros_and_window()
    {
        string[] dirs = [_a, _b];
        MacroLibraryStore.Save(dirs, Sample());

        var loaded = MacroLibraryStore.Load(dirs);
        Assert.NotNull(loaded);
        Assert.Equal("Game", loaded!.Window?.ProcessName);
        Assert.Equal("teclas_1", Assert.Single(loaded.Macros).Id);
    }

    [Fact]
    public void Survives_one_location_being_corrupt()
    {
        string[] dirs = [_a, _b];
        MacroLibraryStore.Save(dirs, Sample());
        File.WriteAllText(Path.Combine(_a, MacroLibraryStore.FileName), "{ \"macros\": [");

        var loaded = MacroLibraryStore.Load(dirs);
        Assert.Equal("teclas_1", Assert.Single(loaded!.Macros).Id);
    }

    [Fact]
    public void Imports_legacy_once_and_then_ignores_it()
    {
        string[] dirs = [_a, _b];
        var first = MacroLibraryStore.LoadOrImport(dirs, Sample);
        Assert.Single(first.Macros);

        var second = MacroLibraryStore.LoadOrImport(dirs, () => throw new InvalidOperationException("no debe reimportar"));
        Assert.Single(second.Macros);
    }

    [Fact]
    public void Failing_to_write_everywhere_throws()
    {
        var file = Path.GetTempFileName(); // un archivo como «carpeta»: no se puede escribir dentro
        try
        {
            Assert.Throws<IOException>(() => MacroLibraryStore.Save([file], Sample()));
        }
        finally
        {
            File.Delete(file);
        }
    }
}
