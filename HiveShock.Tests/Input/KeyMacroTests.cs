using HiveShock.Input;

namespace HiveShock.Tests.Input;

public class KeyMacroTests
{
    private static readonly WindowTarget Game = new() { ProcessName = "game" };

    private static KeyMacro Macro(string id, params KeyStep[] steps) =>
        new() { Id = id, Steps = [.. steps] };

    private static KeyStep Step(string key, KeyAction action = KeyAction.Tap, int hold = 500, int delay = 0, int repeat = 1) =>
        new() { Key = key, Action = action, HoldMs = hold, DelayAfterMs = delay, Repeat = repeat };

    private static async Task<FakeGameInput> RunAsync(Func<KeyMacroRunner, Task> arrange, FakeGameInput? input = null)
    {
        input ??= new FakeGameInput();
        using var runner = new KeyMacroRunner(input, () => Game, delay: (_, _) => Task.CompletedTask);
        using var cts = new CancellationTokenSource();
        var loop = runner.RunAsync(cts.Token);
        await arrange(runner);
        await Task.Delay(100);
        await cts.CancelAsync();
        await loop;
        return input;
    }

    [Fact]
    public async Task Tap_y_hold_pulsan_y_sueltan_en_orden()
    {
        var input = await RunAsync(r =>
        {
            r.Enqueue(Macro("m", Step("W"), Step("Space", KeyAction.Hold, hold: 800)), "u", "t");
            return Task.CompletedTask;
        });

        Assert.Equal(["+W", "-W", "+Space", "-Space"], input.Events);
    }

    [Fact]
    public async Task Repeat_repite_el_paso()
    {
        var input = await RunAsync(r =>
        {
            r.Enqueue(Macro("m", Step("A", repeat: 3)), "u", "t");
            return Task.CompletedTask;
        });

        Assert.Equal(6, input.Events.Count);
    }

    [Fact]
    public async Task Down_sin_up_se_suelta_al_terminar()
    {
        var input = await RunAsync(r =>
        {
            r.Enqueue(Macro("m", Step("Shift", KeyAction.Down), Step("W")), "u", "t");
            return Task.CompletedTask;
        });

        Assert.Equal("-Shift", input.Events[^1]);
    }

    [Fact]
    public async Task Cancelar_a_mitad_suelta_las_teclas_pulsadas()
    {
        var input = new FakeGameInput();
        var gate = new TaskCompletionSource();
        using var runner = new KeyMacroRunner(input, () => Game, delay: (_, ct) => gate.Task.WaitAsync(ct));
        using var cts = new CancellationTokenSource();
        var loop = runner.RunAsync(cts.Token);

        runner.Enqueue(Macro("m", Step("W", KeyAction.Hold, hold: 5000)), "u", "t");
        await Task.Delay(100);
        Assert.Equal(["+W"], input.Events);

        runner.CancelAll();
        await Task.Delay(100);
        await cts.CancelAsync();
        await loop;

        Assert.Equal(["+W", "-W"], input.Events);
    }

    [Fact]
    public async Task Overlap_drop_descarta_la_segunda_activacion()
    {
        var input = new FakeGameInput();
        var gate = new TaskCompletionSource();
        using var runner = new KeyMacroRunner(input, () => Game, delay: (_, ct) => gate.Task.WaitAsync(ct));
        using var cts = new CancellationTokenSource();
        var loop = runner.RunAsync(cts.Token);
        var macro = Macro("m", Step("W", KeyAction.Hold, hold: 5000)) with { Overlap = MacroOverlap.Drop };

        Assert.True(runner.Enqueue(macro, "a", "t"));
        await Task.Delay(100);
        Assert.False(runner.Enqueue(macro, "b", "t"));

        gate.SetResult();
        await Task.Delay(100);
        await cts.CancelAsync();
        await loop;
    }

    [Fact]
    public async Task Ventana_elevada_no_envia_nada()
    {
        var input = await RunAsync(
            r =>
            {
                r.Enqueue(Macro("m", Step("W")), "u", "t");
                return Task.CompletedTask;
            },
            new FakeGameInput { Status = WindowStatus.Elevated });

        Assert.Empty(input.Events);
        Assert.Equal(0, input.Sessions);
    }

    [Fact]
    public async Task Macros_seguidas_comparten_sesion_de_foco()
    {
        var input = await RunAsync(r =>
        {
            r.Enqueue(Macro("a", Step("W")), "u", "t");
            r.Enqueue(Macro("b", Step("S")), "u", "t");
            return Task.CompletedTask;
        });

        Assert.Equal(1, input.Sessions);
    }

    [Fact]
    public void Validador_bloquea_combos_peligrosos()
    {
        var bad = Macro("m", Step("Alt", KeyAction.Down), Step("F4"));
        Assert.NotEmpty(KeyMacroValidator.Validate(bad));
        Assert.Empty(KeyMacroValidator.Validate(bad with { AllowDangerous = true }));
        Assert.NotEmpty(KeyMacroValidator.Validate(Macro("w", Step("Win"))));
    }

    [Fact]
    public void Validador_rechaza_tecla_desconocida_y_exceso_de_duracion()
    {
        Assert.NotEmpty(KeyMacroValidator.Validate(Macro("m", Step("Banana"))));
        Assert.NotEmpty(KeyMacroValidator.Validate(Macro("m", Step("W", KeyAction.Hold, hold: 20_000))));
    }

    [Fact]
    public void Catalogo_guarda_y_recarga_json()
    {
        var path = Path.Combine(Path.GetTempPath(), $"keys-{Guid.NewGuid():N}.json");
        try
        {
            KeyMacroCatalogStore.Save(path, [Macro("saltar", Step("Space", KeyAction.Hold, hold: 300))]);
            var loaded = KeyMacroCatalogStore.Load(path);

            Assert.True(loaded.TryGet("SALTAR", out var m));
            Assert.Equal(KeyAction.Hold, m.Steps[0].Action);
            Assert.Equal(300, m.Steps[0].HoldMs);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".bak");
        }
    }

    [Fact]
    public void Catalogo_con_json_roto_queda_vacio()
    {
        var path = Path.Combine(Path.GetTempPath(), $"keys-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{ no es json");
            Assert.Empty(KeyMacroCatalogStore.Load(path).All);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
