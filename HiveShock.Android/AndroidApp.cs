using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Avalonia.Fonts.Inter;

namespace HiveShock.Android;

[Application(Name = "dev.yafel.hiveshock.AndroidApp")]
public class AndroidApp : AvaloniaAndroidApplication<App>
{
    protected AndroidApp(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    public override void OnCreate()
    {
        AndroidIntents.Register();
        base.OnCreate();
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }
}
