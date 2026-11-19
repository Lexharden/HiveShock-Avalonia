using UIKit;

namespace HiveShock.iOS;

public class Application
{
    // Punto de entrada de la app iOS; AppDelegate arranca Avalonia.
    private static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
}
