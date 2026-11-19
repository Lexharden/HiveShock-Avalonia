using Avalonia;
using Avalonia.Fonts.Inter;
using Avalonia.iOS;
using Foundation;

namespace HiveShock.iOS;

[Register("AppDelegate")]
public class AppDelegate : AvaloniaAppDelegate<App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        IosHost.Register();
        return base.CustomizeAppBuilder(builder).WithInterFont();
    }
}
