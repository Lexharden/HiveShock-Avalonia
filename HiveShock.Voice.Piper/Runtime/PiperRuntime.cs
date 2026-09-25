using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using HiveShock.Logging;

namespace HiveShock.Voice.Piper.Runtime;

/// <summary>Binario de Piper publicado para un sistema: archivo, hash fijado y tamaño aproximado.</summary>
public sealed record PiperRuntimeAsset(OSPlatform Os, Architecture Architecture, string FileName, string Sha256, int ApproxMegabytes)
{
    public bool IsZip => FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Instala y localiza el motor Piper. Se usa la última versión autónoma publicada por
/// rhasspy/piper (MIT, repositorio archivado); el proyecto activo (OHF-Voice/piper1-gpl) ya no
/// publica ejecutables. Los hashes SHA-256 los fija HiveShock porque GitHub no los publica
/// para esa versión: si la descarga no coincide, no se instala.
/// </summary>
public sealed class PiperRuntime
{
    public const string Version = "2023.11.14-2";
    private const string BaseUrl = "https://github.com/rhasspy/piper/releases/download/" + Version + "/";
    private const string MarkerFile = "hiveshock-piper-version.txt";

    public static readonly IReadOnlyList<PiperRuntimeAsset> Assets =
    [
        new(OSPlatform.Windows, Architecture.X64, "piper_windows_amd64.zip",
            "f3c58906402b24f3a96d92145f58acba6d86c9b5db896d207f78dc80811efcea", 22),
        new(OSPlatform.OSX, Architecture.X64, "piper_macos_x64.tar.gz",
            "ced85c0a3df13945b1e623b878a48fdc2854d5c485b4b67f62857cf551deaf8b", 19),
        // Sin verificar en un Mac con Apple Silicon (pesa lo mismo que el de Intel).
        new(OSPlatform.OSX, Architecture.Arm64, "piper_macos_aarch64.tar.gz",
            "6b1eb03b3735946cb35216e063e7eebcc33a6bbf5dd96ec0217959bf1cdcb0cc", 19),
        new(OSPlatform.Linux, Architecture.X64, "piper_linux_x86_64.tar.gz",
            "a50cb45f355b7af1f6d758c1b360717877ba0a398cc8cbe6d2a7a3a26e225992", 26),
        new(OSPlatform.Linux, Architecture.Arm64, "piper_linux_aarch64.tar.gz",
            "fea0fd2d87c54dbc7078d0f878289f404bd4d6eea6e7444a77835d1537ab88eb", 25),
    ];

    private readonly PiperPaths _paths;
    private readonly HttpClient _http;

    public PiperRuntime(PiperPaths paths, HttpClient? http = null)
    {
        _paths = paths;
        _http = http ?? HttpDownloader.SharedClient;
    }

    /// <summary>El binario para este equipo, o null si Piper no publica uno (p. ej. Windows ARM).</summary>
    public static PiperRuntimeAsset? CurrentAsset =>
        Assets.FirstOrDefault(a => RuntimeInformation.IsOSPlatform(a.Os) && a.Architecture == RuntimeInformation.OSArchitecture);

    public bool IsSupported => CurrentAsset != null;

    private string Home => Path.Combine(_paths.RuntimeDirectory, "piper");

    public string ExecutablePath => Path.Combine(Home, OperatingSystem.IsWindows() ? "piper.exe" : "piper");

    public string EspeakDataPath => Path.Combine(Home, "espeak-ng-data");

    /// <summary>Instalado y de la versión esperada. Se cachea: el registro de motores lo consulta a menudo.</summary>
    public bool IsInstalled => _installed ??= CheckInstalled();

    private bool? _installed;

    /// <summary>Se dispara tras instalar o desinstalar.</summary>
    public event Action? Changed;

    private bool CheckInstalled()
    {
        var marker = Path.Combine(_paths.RuntimeDirectory, MarkerFile);
        try
        {
            return File.Exists(ExecutablePath) && File.Exists(marker) && File.ReadAllText(marker).Trim() == Version;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>Descarga, verifica y descomprime el motor. Si algo falla, la instalación anterior queda intacta.</summary>
    public async Task InstallAsync(IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var asset = CurrentAsset ?? throw new PlatformNotSupportedException(
            "Piper no tiene versión para este sistema (solo Windows x64, macOS y Linux x64/ARM64).");

        Directory.CreateDirectory(_paths.TempDirectory);
        var archive = Path.Combine(_paths.TempDirectory, asset.FileName);
        var staging = _paths.RuntimeDirectory + ".new";
        try
        {
            await HttpDownloader.DownloadVerifiedAsync(_http, BaseUrl + asset.FileName, archive, asset.Sha256,
                HashAlgorithmName.SHA256, progress, 0, null, ct).ConfigureAwait(false);

            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }

            Directory.CreateDirectory(staging);
            if (asset.IsZip)
            {
                ZipFile.ExtractToDirectory(archive, staging, overwriteFiles: true);
            }
            else
            {
                await using var file = File.OpenRead(archive);
                await using var gzip = new GZipStream(file, CompressionMode.Decompress);
                // Conserva permisos de ejecución y enlaces simbólicos de las librerías (.so/.dylib).
                await TarFile.ExtractToDirectoryAsync(gzip, staging, overwriteFiles: true, ct).ConfigureAwait(false);
            }

            var exe = Path.Combine(staging, "piper", OperatingSystem.IsWindows() ? "piper.exe" : "piper");
            if (!File.Exists(exe))
            {
                throw new InvalidOperationException("El paquete de Piper no tiene el formato esperado.");
            }

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(exe, File.GetUnixFileMode(exe) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute);
            }

            await File.WriteAllTextAsync(Path.Combine(staging, MarkerFile), Version, ct).ConfigureAwait(false);

            if (Directory.Exists(_paths.RuntimeDirectory))
            {
                Directory.Delete(_paths.RuntimeDirectory, recursive: true);
            }

            Directory.Move(staging, _paths.RuntimeDirectory);
            _installed = null;
            BridgeLog.Info($"Piper {Version} instalado en {_paths.RuntimeDirectory}");
            Changed?.Invoke();
        }
        finally
        {
            TryDelete(archive);
            if (Directory.Exists(staging))
            {
                try
                {
                    Directory.Delete(staging, recursive: true);
                }
                catch
                {
                    // se limpia en la próxima instalación
                }
            }
        }
    }

    public void Uninstall()
    {
        if (Directory.Exists(_paths.RuntimeDirectory))
        {
            Directory.Delete(_paths.RuntimeDirectory, recursive: true);
        }

        _installed = null;
        Changed?.Invoke();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // archivo temporal: no importa
        }
    }
}
