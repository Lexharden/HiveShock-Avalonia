using Foundation;
using HiveShock.Hosting;
using HiveShock.Mobile;
using UIKit;

namespace HiveShock.iOS;

/// <summary>
/// Plataforma iOS para la UI móvil compartida. iOS suspende la app poco después de pasar a segundo plano,
/// así que «mantener vivo» se reduce a impedir que la pantalla se apague mientras el puente está conectado.
/// </summary>
internal sealed class IosHost : IMobileHost
{
    private static BridgeRuntime? _runtime;

    public static IosHost Instance { get; } = new();

    public static void Register() => MobileHost.Current = Instance;

    /// <summary>Un solo puente por proceso, igual que en Android.</summary>
    public BridgeRuntime Runtime => _runtime ??= BridgeRuntime.Create();

    public void OpenUrl(string url)
    {
        if (NSUrl.FromString(url) is not { } nsUrl)
        {
            return;
        }

        UIApplication.SharedApplication.BeginInvokeOnMainThread(() =>
            UIApplication.SharedApplication.OpenUrl(nsUrl, new UIApplicationOpenUrlOptions(), null));
    }

    public void CopyText(string text) =>
        UIApplication.SharedApplication.BeginInvokeOnMainThread(() => UIPasteboard.General.String = text);

    public void StartKeepAlive() =>
        UIApplication.SharedApplication.BeginInvokeOnMainThread(() => UIApplication.SharedApplication.IdleTimerDisabled = true);

    public void StopKeepAlive() =>
        UIApplication.SharedApplication.BeginInvokeOnMainThread(() => UIApplication.SharedApplication.IdleTimerDisabled = false);
}
