using HiveShock.Hosting;
using HiveShock.Mobile;

namespace HiveShock.Android;

/// <summary>Plataforma Android para la UI móvil compartida: reutiliza el puente único, los intents y el servicio en primer plano.</summary>
internal sealed class AndroidHost : IMobileHost
{
    public static AndroidHost Instance { get; } = new();

    public BridgeRuntime Runtime => AndroidBridge.Shared;

    public void OpenUrl(string url) => AndroidIntents.OpenUrl(url);

    public void CopyText(string text) => AndroidIntents.CopyText(text);

    public void StartKeepAlive() => BridgeKeepAliveService.Start();

    public void StopKeepAlive() => BridgeKeepAliveService.Stop();
}
