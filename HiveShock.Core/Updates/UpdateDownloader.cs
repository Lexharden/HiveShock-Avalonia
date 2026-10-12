using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace HiveShock.Updates;

public sealed record DownloadResult(string ZipPath, bool Verified);

/// <summary>Descarga el zip «update» con reanudación y lo verifica (tamaño, SHA-256, zip legible).</summary>
public sealed class UpdateDownloader
{
    private readonly HttpClient _http;

    public UpdateDownloader(HttpClient http) => _http = http;

    public async Task<DownloadResult> DownloadAsync(
        ReleaseInfo release, ReleaseAsset asset, string destDir, IProgress<double>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(destDir);
        var zipPath = Path.Combine(destDir, asset.Name);
        var expected = asset.Sha256 ?? await FindPublishedHashAsync(release, asset, ct).ConfigureAwait(false);

        // Ya descargado y válido: no se repite.
        if (File.Exists(zipPath) && Validate(zipPath, asset, expected) is null)
        {
            progress?.Report(1);
            return new DownloadResult(zipPath, expected is not null);
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            await FetchAsync(asset, zipPath + ".part", progress, ct).ConfigureAwait(false);
            File.Move(zipPath + ".part", zipPath, overwrite: true);

            var problem = Validate(zipPath, asset, expected);
            if (problem is null)
            {
                return new DownloadResult(zipPath, expected is not null);
            }

            TryDelete(zipPath);
            if (attempt == 1)
            {
                throw new UpdateException($"La descarga no es válida ({problem}). No se instaló nada.");
            }
        }

        throw new UpdateException("La descarga no es válida.");
    }

    private async Task<string?> FindPublishedHashAsync(ReleaseInfo release, ReleaseAsset asset, CancellationToken ct)
    {
        var sums = release.FindAsset("SHA256SUMS.txt");
        if (sums is null)
        {
            return null;
        }

        try
        {
            var text = await _http.GetStringAsync(sums.Url, ct).ConfigureAwait(false);
            return GitHubReleaseClient.FindHash(text, asset.Name);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private async Task FetchAsync(ReleaseAsset asset, string partPath, IProgress<double>? progress, CancellationToken ct)
    {
        var have = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;
        if (asset.Size > 0 && have > asset.Size)
        {
            TryDelete(partPath);
            have = 0;
        }

        using var req = new HttpRequestMessage(HttpMethod.Get, asset.Url);
        req.Headers.UserAgent.Add(new ProductInfoHeaderValue("HiveShock-Updater", "1"));
        if (have > 0)
        {
            req.Headers.Range = new RangeHeaderValue(have, null);
        }

        HttpResponseMessage res;
        try
        {
            res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new UpdateException("Se cortó la descarga (¿sin conexión?). Se reanudará al reintentar.", ex);
        }

        using (res)
        {
            if (res.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                TryDelete(partPath);
                throw new UpdateException("La descarga parcial no servía; reintenta.");
            }

            if (!res.IsSuccessStatusCode)
            {
                throw new UpdateException($"La descarga falló ({(int)res.StatusCode}).");
            }

            var resume = res.StatusCode == HttpStatusCode.PartialContent && have > 0;
            if (!resume)
            {
                have = 0;
            }

            var total = asset.Size > 0 ? asset.Size : (res.Content.Headers.ContentLength ?? 0) + have;
            await using var src = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dst = new FileStream(partPath, resume ? FileMode.Append : FileMode.Create,
                FileAccess.Write, FileShare.None, 81920, useAsync: true);

            var buffer = new byte[81920];
            var done = have;
            int read;
            try
            {
                while ((read = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    done += read;
                    if (total > 0)
                    {
                        progress?.Report(Math.Min(1.0, (double)done / total));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException)
            {
                throw new UpdateException("Se cortó la descarga. Se reanudará al reintentar.", ex);
            }
        }

        progress?.Report(1);
    }

    /// <summary>Null si el archivo es correcto; si no, el motivo.</summary>
    public static string? Validate(string zipPath, ReleaseAsset asset, string? expectedSha256)
    {
        try
        {
            var info = new FileInfo(zipPath);
            if (asset.Size > 0 && info.Length != asset.Size)
            {
                return "tamaño distinto al publicado";
            }

            if (expectedSha256 is not null)
            {
                using var fs = File.OpenRead(zipPath);
                var hash = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
                if (!string.Equals(hash, expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    return "la suma SHA-256 no coincide";
                }
            }

            using var zip = ZipFile.OpenRead(zipPath);
            return zip.Entries.Count == 0 ? "el zip está vacío" : null;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            return "el zip está dañado";
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // ignore
        }
    }
}
