using System.Text.Json;
using HiveShock.Configuration;
using HiveShock.Input;
using HiveShock.Networking;

namespace HiveShock.Tests.Input;

/// <summary>Las macros de teclas viven en el catálogo de efectos: el dispatcher las manda al runner, no al juego por TCP.</summary>
public class KeyMacroDispatchTests
{
    private static readonly KeyMacro Jump = new()
    {
        Id = "saltar",
        Label = "Saltar",
        Steps = [new KeyStep { Key = "Space", Action = KeyAction.Tap }],
    };

    private static EffectCatalog Catalog(params KeyMacro[] macros)
    {
        var impulse = JsonDocument.Parse("""{ "action": "impulse" }""").RootElement
            .EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
        return new EffectCatalog(
            new Dictionary<string, Dictionary<string, JsonElement>> { ["impulse"] = impulse },
            macros: new KeyMacroCatalog(macros));
    }

    [Fact]
    public void El_catalogo_expone_las_macros_como_efectos()
    {
        var effects = Catalog(Jump);

        Assert.True(effects.Contains("saltar"));
        Assert.Equal("keys", effects.GetAction("saltar"));
        Assert.Equal("Saltar", effects.Describe("saltar"));
        Assert.Contains("saltar", effects.Ids);
        Assert.Equal(2, effects.Count);
        Assert.Null(effects.Resolve("saltar", "u"));
        Assert.False(effects.TryGetMacro("impulse", out _));
    }

    [Fact]
    public void Un_efecto_de_effects_json_gana_a_una_macro_con_el_mismo_id()
    {
        var effects = Catalog(Jump with { Id = "impulse" });

        Assert.False(effects.TryGetMacro("impulse", out _));
        Assert.Equal(1, effects.Count);
    }

    [Fact]
    public async Task El_dispatcher_manda_la_macro_al_runner()
    {
        var input = new FakeGameInput();
        using var runner = new KeyMacroRunner(input, () => new WindowTarget { ProcessName = "game" }, delay: (_, _) => Task.CompletedTask);
        using var cts = new CancellationTokenSource();
        var loop = Task.Run(() => runner.RunAsync(cts.Token));
        var dispatcher = new EffectDispatcher(
            Catalog(Jump),
            new GameTcpClient(new BridgeOptions { EffectGapMs = 0 }),
            keys: runner);

        await dispatcher.EnqueueAsync("saltar", "viewer", "chat");
        await Task.Delay(150);
        await cts.CancelAsync();
        await loop;

        Assert.Equal(["+Space", "-Space"], input.Events);
    }

    [Fact]
    public void Profile_info_lee_la_ventana_objetivo_del_json()
    {
        var info = JsonSerializer.Deserialize<GameProfileInfo>(
            """{ "id": "x", "window": { "processName": "soh", "titleContains": "Ship" } }""",
            JsonDefaults.Options);

        Assert.Equal("soh", info!.Window!.ProcessName);
        Assert.Equal("Ship", info.Window.TitleContains);
        Assert.False(info.Window.IsEmpty);
    }
}
