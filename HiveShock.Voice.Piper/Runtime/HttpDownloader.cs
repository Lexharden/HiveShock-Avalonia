using System.Net.Http;
using System.Security.Cryptography;

namespace HiveShock.Voice.Piper.Runtime;

/// <summary>Progreso de una descarga. Total puede ser desconocido si el servidor no lo informa.</summary>
public readonly record struct DownloadProgress(long Received, long? Total)
{
    public double? Fraction => Total is > 0 ? Math.Clamp(Received / (double)Total.Value, 0, 1) : null;
}

/// <summary>
/// Descarga a un archivo temporal ".part", verifica el hash y solo entonces lo pone en su
/// sitio: nunca queda un archivo a medias con el nombre bueno. Solo se usan URLs fijadas en
/// el código (GitHub releases de una versión concreta y el repositorio oficial de voces).
/// </summary>
internal static class HttpDownloader
{
    public static readonly HttpClient SharedClient = CreateClient();

    public static async Task DownloadVerifiedAsync(
        HttpClient http,
        string url,
        string destination,
        string expectedHash,
        HashAlgorithmName algorithm,
        IProgress<DownloadProgress>? progress,
        long progressOffset,
        long? progressTotal,
        CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var part = destination + ".part";
        try
        {
            using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var total = progressTotal ?? response.Content.Headers.ContentLength;
                await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var target = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                var buffer = new byte[81920];
                long received = 0;
                var lastReport = DateTime.MinValue;
                int read;
                while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    received += read;
                    if (progress != null && DateTime.UtcNow - lastReport > TimeSpan.FromMilliseconds(100))
                    {
                        lastReport = DateTime.UtcNow;
                        progress.Report(new DownloadProgress(progressOffset + received, total));
                    }
                }

                progress?.Report(new DownloadProgress(progressOffset + received, total));
            }

            var actual = await ComputeHashAsync(part, algorithm, ct).ConfigureAwait(false);
            if (!string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "El archivo descargado no coincide con el original (puede estar dañado o haber sido modificado). Inténtalo de nuevo.");
            }

            File.Move(part, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(part))
            {
                File.Delete(part);
            }
        }
    }

    public static async Task<string> ComputeHashAsync(string path, HashAlgorithmName algorithm, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        byte[] hash;
        if (algorithm == HashAlgorithmName.MD5)
        {
            hash = await MD5.HashDataAsync(stream, ct).ConfigureAwait(false);
        }
        else if (algorithm == HashAlgorithmName.SHA256)
        {
            hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Solo MD5 o SHA-256.");
        }

        return Convert.ToHexString(hash);
    }

    private static HttpClient CreateClient()
    {
        // Sin timeout global: las descargas grandes se cortan por su CancellationToken, no a los 100 s.
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("HiveShock (+https://hiveshock.yafel.dev)");
        return client;
    }
}
