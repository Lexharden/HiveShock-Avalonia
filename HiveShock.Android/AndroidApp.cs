using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Avalonia.Fonts.Inter;

namespace HiveShock.Android;

/// <summary>
/// Bootstrap Avalonia 12. Sin Name custom: el tooling genera el CRC estable
/// y registra JNI (evita UnsatisfiedLinkError en AvaloniaAndroidApplication.OnCreate).
/// </summary>
[Application]
public class AndroidApp : AvaloniaAndroidApplication<App>
{
    protected AndroidApp(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        AndroidIntents.Register();
        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }
}
