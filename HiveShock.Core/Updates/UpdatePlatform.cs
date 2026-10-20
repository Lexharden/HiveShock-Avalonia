using System.Runtime.InteropServices;

namespace HiveShock.Updates;

public static class UpdatePlatform
{
    /// <summary>Identificador de plataforma de los assets: win-x64, osx-arm64, osx-x64, linux-x64, linux-arm64.</summary>
    public static string CurrentRid()
    {
        var arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            var other => other.ToString().ToLowerInvariant(),
        };

        var os = OperatingSystem.IsWindows() ? "win"
            : OperatingSystem.IsMacOS() ? "osx"
            : OperatingSystem.IsLinux() ? "linux"
            : "unknown";
        return $"{os}-{arch}";
    }

    public static string UpdateAssetName(string version, string rid) => $"HiveShock-{version}-{rid}-update.zip";

    /// <summary>El zip «update» de esta plataforma, o null si el release no lo trae.</summary>
    public static ReleaseAsset? SelectAsset(ReleaseInfo release, string? rid = null) =>
        release.FindAsset(UpdateAssetName(release.Version, rid ?? CurrentRid()));
}
