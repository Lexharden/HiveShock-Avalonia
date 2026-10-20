using HiveShock.Platform.Windows;

namespace HiveShock.Input;

public static class GameInputFactory
{
    public static IGameInput Create() =>
        OperatingSystem.IsWindows() ? new Win32GameInput() : new NullGameInput();

    public static IGlobalHotkeys CreateHotkeys() =>
        OperatingSystem.IsWindows() ? new Win32Hotkeys() : new NullGlobalHotkeys();
}
