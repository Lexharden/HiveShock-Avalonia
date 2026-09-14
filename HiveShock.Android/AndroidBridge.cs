using HiveShock.Hosting;

namespace HiveShock.Android;

/// <summary>
/// Un solo <see cref="BridgeRuntime"/> por proceso. Si Android recrea la Activity,
/// la UI se engancha al mismo puente (TikTok/Twitch no se cortan).
/// </summary>
internal static class AndroidBridge
{
    private static BridgeRuntime? _runtime;

    public static BridgeRuntime Shared => _runtime ??= BridgeRuntime.Create();
}
