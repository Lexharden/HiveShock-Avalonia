using System.Text.Json;
using HiveShock.Configuration;
using HiveShock.Hosting;
using HiveShock.Networking;
using HiveShock.Platform;
using HiveShock.Tests.Input;

namespace HiveShock.Tests.Hosting;

public class SystemEffectTests
{
    private sealed class FakePower : IPowerControl
    {
        public bool IsSupported { get; init; } = true;

        public List<bool> Restarts { get; } = [];

        public void Restart(bool force) => Restarts.Add(force);
    }

    private static SystemEffectSettings Settings(bool force = false) => new() { CountdownSec = 10, Force = force };

    private static SystemEffectService Service(
        FakePower power,
        bool dryRun = false,
        Func<bool>? paused = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        SystemEffectSettings? settings = null) =>
        new(power, () => dryRun, paused ?? (() => false), delay ?? ((_, _) => Task.CompletedTask), settings ?? Settings());

    private static async Task Finished(SystemEffectService service) =>
        await Wait.UntilAsync(() => !service.IsCountingDown);

    private static EffectCatalog EmptyCatalog() => new(new Dictionary<string, Dictionary<string, JsonElement>>());

    [Fact]
    public async Task Disarmed_by_default_and_ignores_the_trigger()
    {
        var power = new FakePower();
        using var service = Service(power);

        Assert.False(service.Armed);
        Assert.False(service.Trigger(SystemEffectIds.RestartPc, "ana", "regalo"));
        await Task.Delay(50);
        Assert.Empty(power.Restarts);
    }

    [Fact]
    public async Task Armed_counts_down_then_restarts_once_per_session()
    {
        var power = new FakePower();
        using var service = Service(power);
        service.SetArmed(true);

        Assert.True(service.Trigger(SystemEffectIds.RestartPc, "ana", "regalo"));
        await Finished(service);

        Assert.Equal([false], power.Restarts);
        Assert.True(service.UsedThisSession);
        Assert.False(service.Trigger(SystemEffectIds.RestartPc, "luis", "regalo"));
        Assert.Single(power.Restarts);
    }

    [Fact]
    public async Task Force_setting_is_passed_to_the_restart()
    {
        var power = new FakePower();
        using var service = Service(power, settings: Settings(force: true));
        service.SetArmed(true);

        service.Trigger(SystemEffectIds.RestartPc, "ana", "regalo");
        await Finished(service);

        Assert.Equal([true], power.Restarts);
    }

    [Fact]
    public async Task Cancel_during_the_countdown_prevents_the_restart()
    {
        var power = new FakePower();
        var gate = new TaskCompletionSource();
        using var service = Service(power, delay: (_, ct) => gate.Task.WaitAsync(ct));
        service.SetArmed(true);

        Assert.True(service.Trigger(SystemEffectIds.RestartPc, "ana", "regalo"));
        await Wait.UntilAsync(() => service.SecondsLeft > 0);
        Assert.True(service.IsCountingDown);

        Assert.True(service.Cancel("prueba"));
        await Finished(service);

        Assert.Empty(power.Restarts);
        Assert.False(service.UsedThisSession);
        Assert.False(service.Cancel("otra vez"));
    }

    [Fact]
    public async Task Second_trigger_while_counting_is_ignored()
    {
        var power = new FakePower();
        var gate = new TaskCompletionSource();
        using var service = Service(power, delay: (_, ct) => gate.Task.WaitAsync(ct));
        service.SetArmed(true);

        Assert.True(service.Trigger(SystemEffectIds.RestartPc, "ana", "regalo"));
        Assert.False(service.Trigger(SystemEffectIds.RestartPc, "luis", "regalo"));

        service.Cancel("fin");
        await Finished(service);
    }

    [Fact]
    public async Task Dry_run_counts_down_but_never_restarts()
    {
        var power = new FakePower();
        using var service = Service(power, dryRun: true);
        service.SetArmed(true);

        Assert.True(service.Trigger(SystemEffectIds.RestartPc, "ana", "regalo"));
        await Finished(service);

        Assert.Empty(power.Restarts);
    }

    [Fact]
    public async Task Preview_works_without_arming_and_never_restarts()
    {
        var power = new FakePower();
        using var service = Service(power);

        Assert.True(service.Trigger(SystemEffectIds.RestartPc, "tú", "prueba", preview: true));
        await Finished(service);

        Assert.Empty(power.Restarts);
        Assert.False(service.UsedThisSession);
    }

    [Fact]
    public async Task Emergency_pause_blocks_and_cancels()
    {
        var power = new FakePower();
        var paused = false;
        var gate = new TaskCompletionSource();
        using var service = Service(power, paused: () => paused, delay: (_, ct) => gate.Task.WaitAsync(ct));
        service.SetArmed(true);

        paused = true;
        Assert.False(service.Trigger(SystemEffectIds.RestartPc, "ana", "regalo"));

        paused = false;
        Assert.True(service.Trigger(SystemEffectIds.RestartPc, "ana", "regalo"));
        await Wait.UntilAsync(() => service.SecondsLeft > 0);
        paused = true;
        gate.SetResult(); // pasa un segundo y se comprueba la pausa
        await Finished(service);

        Assert.Empty(power.Restarts);
    }

    [Fact]
    public async Task Disarming_cancels_a_running_countdown()
    {
        var power = new FakePower();
        var gate = new TaskCompletionSource();
        using var service = Service(power, delay: (_, ct) => gate.Task.WaitAsync(ct));
        service.SetArmed(true);
        service.Trigger(SystemEffectIds.RestartPc, "ana", "regalo");
        await Wait.UntilAsync(() => service.SecondsLeft > 0);

        service.SetArmed(false);
        await Finished(service);

        Assert.Empty(power.Restarts);
    }

    [Fact]
    public async Task Unsupported_system_ignores_everything()
    {
        var power = new FakePower { IsSupported = false };
        using var service = Service(power);
        service.SetArmed(true);

        Assert.False(service.Trigger(SystemEffectIds.RestartPc, "ana", "regalo", preview: true));
        await Task.Delay(50);
        Assert.Empty(power.Restarts);
        Assert.Empty(SystemEffectIds.Supported(power));
    }

    [Fact]
    public void Catalog_lists_the_system_effect_only_when_enabled()
    {
        var effects = EmptyCatalog();
        Assert.False(effects.Contains(SystemEffectIds.RestartPc));

        effects.SetSystemEffects([SystemEffectIds.RestartPc]);
        Assert.True(effects.Contains(SystemEffectIds.RestartPc));
        Assert.Equal("system", effects.GetAction(SystemEffectIds.RestartPc));
        Assert.Contains(SystemEffectIds.RestartPc, effects.Ids);
        Assert.Contains("Reiniciar", effects.Describe(SystemEffectIds.RestartPc));
        Assert.Null(effects.Resolve(SystemEffectIds.RestartPc, "u"));
    }

    [Fact]
    public async Task Dispatcher_sends_the_effect_to_the_service_not_to_the_game()
    {
        var power = new FakePower();
        using var service = Service(power);
        service.SetArmed(true);
        var effects = EmptyCatalog();
        effects.SetSystemEffects([SystemEffectIds.RestartPc]);
        using var dispatcher = new EffectDispatcher(
            effects,
            new GameTcpClient(new BridgeOptions { EffectGapMs = 0 }),
            system: service);

        await dispatcher.EnqueueAsync(SystemEffectIds.RestartPc, "ana", "regalo Lion");
        await Wait.UntilAsync(() => power.Restarts.Count > 0);

        Assert.Single(power.Restarts);
    }

    [Fact]
    public void Cancel_hotkey_is_a_valid_global_shortcut()
    {
        Assert.True(HiveShock.Input.HotkeyCombo.TryParse(SystemEffectService.CancelHotkey, out _));
    }

    [Fact]
    public void Windows_power_control_points_at_the_real_shutdown_exe()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.True(new HiveShock.Platform.Windows.Win32PowerControl().IsSupported);
        Assert.True(File.Exists(Path.Combine(Environment.SystemDirectory, "shutdown.exe")));
    }

    [Fact]
    public void Countdown_setting_is_clamped()
    {
        Assert.Equal(SystemEffectSettings.MinCountdownSec, new SystemEffectSettings { CountdownSec = 1 }.ClampedCountdown);
        Assert.Equal(SystemEffectSettings.MaxCountdownSec, new SystemEffectSettings { CountdownSec = 99999 }.ClampedCountdown);
        Assert.Equal(30, new SystemEffectSettings().ClampedCountdown);
    }
}
