using System.Text;
using HiveShock.Hosting;
using HiveShock.Logging;
using HiveShock.Ui;

Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
Console.InputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

await using var runtime = BridgeRuntime.Create(args);
var options = runtime.Options;

if (options.ListEffectsOnly)
{
    foreach (var (id, label) in runtime.Effects.ListEntries())
    {
        Console.WriteLine($"{id,-16}  {label}");
    }

    return;
}

if (!options.SkipMenu)
{
    var menu = new ConsoleMenu(
        options,
        runtime.Effects,
        runtime.Gifts,
        runtime.Catalog,
        runtime.GiftsPath,
        runtime.EffectsPath);
    if (!menu.Run())
    {
        return;
    }
}

var mode = options.DevMode
    ? BridgeRunMode.Sdk
    : options.CaptureOnly
        ? BridgeRunMode.Capture
        : BridgeRunMode.Live;

Console.WriteLine("=================================================");
Console.WriteLine("  HiveShock");
Console.WriteLine("  TikTok Live → efectos en el juego");
Console.WriteLine("  Dev Yafel GH");
Console.WriteLine("=================================================");
Console.WriteLine($"Perfil {runtime.Profile.DisplayName} ({runtime.Profile.Id})");
if (mode == BridgeRunMode.Sdk)
{
    Console.WriteLine("Modo   Pruebas");
    Console.WriteLine($"Pruebas 127.0.0.1:{options.CrowdControlPort}");
}
else if (mode == BridgeRunMode.Capture)
{
    Console.WriteLine("Modo   Captura");
    Console.WriteLine($"Canal  @{options.TikTokUniqueId}");
    Console.WriteLine($"Catálogo  {runtime.Catalog.Count}");
}
else
{
    Console.WriteLine("Modo   En vivo");
    Console.WriteLine($"Canal  @{options.TikTokUniqueId}");
}

if (mode != BridgeRunMode.Capture)
{
    Console.WriteLine($"Juego  {options.GameHost}:{options.GamePort}{(options.DryRun ? "  dry-run" : "")}");
}

if (mode == BridgeRunMode.Live)
{
    Console.WriteLine();
    runtime.Gifts.PrintRules();
}

Console.WriteLine();
Console.WriteLine("Ctrl+C para detener.");
Console.WriteLine();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    _ = runtime.StopAsync();
};

try
{
    await runtime.StartAsync(mode).ConfigureAwait(false);
    await runtime.WaitUntilStoppedAsync().ConfigureAwait(false);
}
finally
{
    BridgeLog.Close();
}
