using System.Collections.Concurrent;
using System.Text.Json;
using HiveShock.Configuration;
using HiveShock.Logging;
using HiveShock.Networking;
using HiveShock.Hosting;
using HiveShock.Tests.Input;

namespace HiveShock.Tests.Configuration;

public sealed class EffectPlanTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hiveshock-plan-" + Guid.NewGuid().ToString("N"));

    public EffectPlanTests() => Directory.CreateDirectory(_dir);

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

    private static EffectCatalog Catalog()
    {
        var map = new Dictionary<string, Dictionary<string, JsonElement>>();
        foreach (var id in new[] { "a", "b", "c", "heal" })
        {
            map[id] = JsonDocument.Parse($$"""{ "action": "{{id}}" }""").RootElement
                .EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
        }

        return new EffectCatalog(map);
    }

    private GiftConfigStore Load(string giftsJson)
    {
        var path = Path.Combine(_dir, "gifts.json");
        File.WriteAllText(path, giftsJson);
        var store = new GiftConfigStore(Catalog());
        store.Reload(path);
        return store;
    }

    [Fact]
    public void A_gift_with_only_effect_stays_a_single_step_plan()
    {
        var store = Load("""{ "gifts": [ { "gift": "Rose", "effect": "a", "params": { "value": 4 } } ] }""");

        var plan = store.ResolveGift("Rose", null, 1)!.EffectivePlan;

        var step = Assert.Single(plan.Steps);
        Assert.Equal("a", step.EffectId);
        Assert.Equal(4, step.Params!["value"]);
        Assert.Equal(0, step.DelayMs);
    }

    [Fact]
    public void A_gift_can_list_several_effects_in_order_with_their_own_params_and_waits()
    {
        var store = Load("""
            { "gifts": [ { "gift": "Doughnut", "also": ["Dona"], "effect": "a", "effects": [
                { "effect": "a", "delaySec": 5 },
                { "effect": "b", "params": { "value": 8 }, "delaySec": 2.5 },
                "c"
            ] } ] }
            """);

        var match = store.ResolveGift("Doughnut", null, 1)!;
        var steps = match.EffectivePlan.Steps;

        Assert.Equal(["a", "b", "c"], steps.Select(s => s.EffectId));
        Assert.Equal(0, steps[0].DelayMs); // el primero sale al llegar el regalo
        Assert.Equal(2500, steps[1].DelayMs);
        Assert.Equal(8, steps[1].Params!["value"]);
        Assert.Equal("a", match.EffectId);
        Assert.Equal(3, store.ResolveGift("Dona", null, 1)!.EffectivePlan.Steps.Count); // los alias comparten el plan
    }

    [Fact]
    public void An_unknown_step_is_skipped_and_a_gift_with_none_valid_is_dropped()
    {
        var store = Load("""
            { "gifts": [
                { "gift": "Mixto", "effects": [ "a", "no_existe", "c" ] },
                { "gift": "Roto", "effects": [ "no_existe" ] }
            ] }
            """);

        Assert.Equal(["a", "c"], store.ResolveGift("Mixto", null, 1)!.EffectivePlan.Steps.Select(s => s.EffectId));
        Assert.Null(store.ResolveGift("Roto", null, 1));
    }

    [Fact]
    public void Plan_is_bounded_and_the_system_effect_always_goes_last()
    {
        var many = Enumerable.Range(0, 20).Select(i => new EffectStep("e" + i, null, 999_999)).ToList();
        var capped = EffectPlan.FromSteps(many);
        Assert.Equal(EffectPlan.MaxSteps, capped.Steps.Count);
        Assert.All(capped.Steps, s => Assert.Equal(EffectPlan.MaxDelaySec * 1000, s.DelayMs));

        var ordered = EffectPlan.FromSteps([new EffectStep("reiniciar_pc"), new EffectStep("a"), new EffectStep("b")]);
        Assert.Equal(["a", "b", "reiniciar_pc"], ordered.Steps.Select(s => s.EffectId));
    }

    [Fact]
    public void Each_repeats_the_whole_plan_per_gift_and_once_only_effects_run_once()
    {
        var plan = EffectPlan.FromSteps([new EffectStep("a"), new EffectStep("b"), new EffectStep("save")]);

        var items = PlanExpander.Expand(plan, 2, each: true, "Dona", s => s.EffectId == "save",
            out var rounds, out var allOnce, out var truncated);

        Assert.Equal(["a", "b", "save", "a", "b"], items.Select(i => i.Step.EffectId));
        Assert.Equal(2, rounds);
        Assert.False(allOnce);
        Assert.False(truncated);
        Assert.Equal("Regalo Dona x2 (1/2) [1/3]", items[0].Reason);
    }

    [Fact]
    public void Without_each_the_plan_runs_once_and_a_big_combo_is_capped()
    {
        var plan = EffectPlan.FromSteps([new EffectStep("a"), new EffectStep("b"), new EffectStep("c")]);

        var once = PlanExpander.Expand(plan, 7, each: false, "Dona", _ => false, out var rounds, out _, out _);
        Assert.Equal(3, once.Count);
        Assert.Equal(1, rounds);

        var big = PlanExpander.Expand(plan, 500, each: true, "Dona", _ => false, out var r2, out _, out var truncated);
        Assert.Equal(100, r2);
        Assert.Equal(EffectPlan.MaxTotalSteps, big.Count);
        Assert.True(truncated);
    }

    [Fact]
    public void A_single_effect_gift_is_saved_exactly_like_before_and_several_add_the_list()
    {
        var path = Path.Combine(_dir, "edit.json");
        File.WriteAllText(path, """{ "gifts": [ { "gift": "Rose", "effect": "a" } ] }""");
        var editor = new GiftFileEditor(path);
        editor.Load();

        editor.Save();
        Assert.DoesNotContain("\"effects\"", File.ReadAllText(path));

        var gift = editor.Gifts[0];
        gift.Params["value"] = 4;
        gift.MoreSteps.Add(new EditableGiftStep { Effect = "b", DelaySec = 2 });
        gift.MoreSteps.Add(new EditableGiftStep { Effect = "c" });
        editor.Save();

        var text = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(text);
        var row = doc.RootElement.GetProperty("gifts")[0];
        Assert.Equal("a", row.GetProperty("effect").GetString()); // lo que ve una versión antigua
        Assert.Equal(3, row.GetProperty("effects").GetArrayLength());

        var again = new GiftFileEditor(path);
        again.Load();
        var loaded = again.Gifts[0];
        Assert.Equal("a", loaded.Effect);
        Assert.Equal(4, loaded.Params["value"]);
        Assert.Equal(["b", "c"], loaded.MoreSteps.Select(s => s.Effect));
        Assert.Equal(2, loaded.MoreSteps[0].DelaySec);

        // y el motor lo lee igual
        var store = new GiftConfigStore(Catalog());
        store.Reload(path);
        Assert.Equal(["a", "b", "c"], store.ResolveGift("Rose", null, 1)!.EffectivePlan.Steps.Select(s => s.EffectId));
    }

    [Fact]
    public async Task Dispatcher_sends_the_steps_in_order_and_a_wait_does_not_block_others()
    {
        var logged = new ConcurrentQueue<string>();
        void OnLog(LogEntry e) => logged.Enqueue(e.Message);
        BridgeLog.Logged += OnLog;
        try
        {
            var options = new BridgeOptions { DryRun = true, EffectGapMs = 0 };
            using var dispatcher = new EffectDispatcher(Catalog(), new GameTcpClient(options));
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var pump = dispatcher.RunAsync(cts.Token);

            dispatcher.EnqueueSequence(
            [
                new PlannedEffect(new EffectStep("a"), "r1"),
                new PlannedEffect(new EffectStep("b"), "r2"),
                new PlannedEffect(new EffectStep("c", null, 600), "r3"),
            ], "plan_user", cts.Token);
            await dispatcher.EnqueueAsync("heal", "otro_espectador", "r4", cts.Token);

            await Wait.UntilAsync(() => logged.Any(m => m.StartsWith("OK c <- plan_user ", StringComparison.Ordinal)));

            var order = logged
                .Where(m => m.StartsWith("OK ", StringComparison.Ordinal) &&
                            (m.Contains("<- plan_user ") || m.Contains("<- otro_espectador ")))
                .Select(m => m.Split(' ')[1])
                .ToList();
            Assert.Equal(["a", "b", "heal", "c"], order);

            cts.Cancel();
            try
            {
                await pump;
            }
            catch (OperationCanceledException)
            {
            }
        }
        finally
        {
            BridgeLog.Logged -= OnLog;
        }
    }

    [Fact]
    public async Task Steps_still_waiting_are_dropped_when_effects_are_paused()
    {
        var logged = new ConcurrentQueue<string>();
        void OnLog(LogEntry e) => logged.Enqueue(e.Message);
        BridgeLog.Logged += OnLog;
        try
        {
            var options = new BridgeOptions { DryRun = true, EffectGapMs = 0 };
            using var dispatcher = new EffectDispatcher(Catalog(), new GameTcpClient(options));
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var pump = dispatcher.RunAsync(cts.Token);

            dispatcher.EnqueueSequence(
            [
                new PlannedEffect(new EffectStep("a"), "p1"),
                new PlannedEffect(new EffectStep("b", null, 400), "p2"),
            ], "pausado_user", cts.Token);
            await Wait.UntilAsync(() => logged.Any(m => m.StartsWith("OK a <- pausado_user ", StringComparison.Ordinal)));

            dispatcher.DiscardPending(); // lo mismo que hace la pausa de emergencia
            await Wait.UntilAsync(() => logged.Any(m => m.StartsWith("Descartado b <- pausado_user ", StringComparison.Ordinal)));

            Assert.DoesNotContain(logged, m => m.StartsWith("OK b <- pausado_user ", StringComparison.Ordinal));

            cts.Cancel();
            try
            {
                await pump;
            }
            catch (OperationCanceledException)
            {
            }
        }
        finally
        {
            BridgeLog.Logged -= OnLog;
        }
    }
}
