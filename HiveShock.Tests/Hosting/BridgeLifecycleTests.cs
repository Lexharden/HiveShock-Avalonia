using HiveShock.Configuration;
using HiveShock.Hosting;
using HiveShock.Input;
using HiveShock.Live;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HiveShock.Tests.Hosting;

/// <summary>Arrancar y parar el puente. Registro en memoria y host propio: no toca datos del usuario ni canales.</summary>
public sealed class BridgeLifecycleTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    private static BridgeRuntime Runtime()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hiveshock-test-{Guid.NewGuid():N}");
        var effects = new EffectCatalog([]);
        var profile = new LoadedGameProfile
        {
            Info = new GameProfileInfo(),
            Directory = dir,
            EffectsPath = Path.Combine(dir, "effects.json"),
            GiftsPath = Path.Combine(dir, "gifts.json"),
            ProfileJsonPath = Path.Combine(dir, "profile.json"),
        };
        return new BridgeRuntime(
            new BridgeOptions { EventPort = 0 },
            profile,
            effects,
            new GiftConfigStore(effects),
            new GiftCatalogStore(Path.Combine(dir, "gift-catalog.json")),
            MacroLibrary.Empty,
            new GiftLedger());
    }

    private static IHost Host(Func<Task>? onStop = null)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IHostedService>(new Probe(onStop));
        return builder.Build();
    }

    private static async Task StartAsync(BridgeRuntime runtime, IHost host)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStarted.Register(() => started.TrySetResult());
        await runtime.StartHostAsync(BridgeRunMode.Sdk, () => host);
        await started.Task.WaitAsync(Limit);
    }

    [Fact]
    public async Task Host_that_ends_on_its_own_is_no_longer_running_and_can_start_again()
    {
        await using var runtime = Runtime();
        var host = Host();
        await StartAsync(runtime, host);
        Assert.True(runtime.IsRunning);
        var stopped = runtime.WaitUntilStoppedAsync();

        host.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();
        await stopped.WaitAsync(Limit);

        Assert.False(runtime.IsRunning);
        Assert.Null(runtime.ActiveMode);
        await StartAsync(runtime, Host());
        Assert.True(runtime.IsRunning);
    }

    [Fact]
    public async Task Host_that_fails_to_start_is_no_longer_running()
    {
        await using var runtime = Runtime();
        var host = new FailingHost();
        await runtime.StartHostAsync(BridgeRunMode.Sdk, () => host);
        var stopped = runtime.WaitUntilStoppedAsync();
        host.Fail.SetResult();

        await Assert.ThrowsAsync<InvalidOperationException>(() => stopped.WaitAsync(Limit));
        Assert.False(runtime.IsRunning);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public async Task Effect_that_arrives_while_stopping_is_kept_only_when_closing_the_app(bool keepPending, int expected)
    {
        await using var runtime = Runtime();
        var added = 0;
        await StartAsync(runtime, Host(() =>
        {
            // Al cancelar, el host se para por dos caminos (RunAsync y StopAsync): el efecto entra una sola vez.
            if (Interlocked.Exchange(ref added, 1) == 0)
            {
                runtime.Ledger.AddPending("impulse", "late_viewer", "test", fromChat: false, parameters: null);
            }

            return Task.CompletedTask;
        }));

        await runtime.StopAsync(keepPending).WaitAsync(Limit);

        Assert.Equal(expected, runtime.Ledger.PendingCount);
    }

    [Fact]
    public async Task Starting_waits_until_the_previous_stop_is_done()
    {
        await using var runtime = Runtime();
        var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await StartAsync(runtime, Host(() =>
        {
            stopping.TrySetResult();
            return release.Task;
        }));

        var stop = runtime.StopAsync();
        await stopping.Task.WaitAsync(Limit);
        var built = false;
        var start = runtime.StartHostAsync(BridgeRunMode.Sdk, () =>
        {
            built = true;
            return Host();
        });

        Assert.False(built);
        release.SetResult();
        await stop.WaitAsync(Limit);
        await start.WaitAsync(Limit);
        Assert.True(built);
        Assert.True(runtime.IsRunning);
    }

    private sealed class Probe(Func<Task>? onStop) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => onStop?.Invoke() ?? Task.CompletedTask;
    }

    private sealed class FailingHost : IHost
    {
        public TaskCompletionSource Fail { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IServiceProvider Services { get; } = new ServiceCollection().BuildServiceProvider();

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            await Fail.Task;
            throw new InvalidOperationException("boom");
        }

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Dispose()
        {
        }
    }
}
