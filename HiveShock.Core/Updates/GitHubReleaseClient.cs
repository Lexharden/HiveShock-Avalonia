using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace HiveShock.Updates;

public sealed record ReleaseFetchResult(IReadOnlyList<ReleaseInfo> Releases, string? ETag, bool NotModified);

/// <summary>Lee los Releases de GitHub. El HttpClient se inyecta para poder probarlo sin red.</summary>
public sealed class GitHubReleaseClient
{
    public const string DefaultRepo = "Lexharden/HiveShock-Avalonia";
    public const string DefaultApiBase = "https://api.github.com";

    private readonly HttpClient _http;
    private readonly string _apiBase;
    private readonly string _repo;

    public GitHubReleaseClient(HttpClient http, string? repo = null, string? apiBase = null)
    {
        _http = http;
        _repo = string.IsNullOrWhiteSpace(repo) ? DefaultRepo : repo.Trim();
        _apiBase = (string.IsNullOrWhiteSpace(apiBase) ? DefaultApiBase : apiBase.Trim()).TrimEnd('/');
    }

    /// <summary>Repositorio y API desde HIVESHOCK_UPDATE_REPO / HIVESHOCK_UPDATE_API (pruebas con un servidor local).</summary>
    public static GitHubReleaseClient FromEnvironment(HttpClient http) =>
        new(http,
            Environment.GetEnvironmentVariable("HIVESHOCK_UPDATE_REPO"),
            Environment.GetEnvironmentVariable("HIVESHOCK_UPDATE_API"));

    public async Task<ReleaseFetchResult> FetchAsync(string? etag, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{_apiBase}/repos/{_repo}/releases?per_page=30");
        req.Headers.UserAgent.Add(new ProductInfoHeaderValue("HiveShock-Updater", "1"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        if (!string.IsNullOrEmpty(etag) && EntityTagHeaderValue.TryParse(etag, out var tag))
        {
            req.Headers.IfNoneMatch.Add(tag);
        }

        HttpResponseMessage res;
        try
        {
            res = await _http.SendAsync(req, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new UpdateException("No se pudo contactar con GitHub (¿sin conexión?).", ex);
        }

        using (res)
        {
            if (res.StatusCode == HttpStatusCode.NotModified)
            {
                return new ReleaseFetchResult([], etag, true);
            }

            if (res.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                throw new UpdateException("GitHub limitó las consultas; se reintentará más tarde.");
            }

            if (!res.IsSuccessStatusCode)
            {
                throw new UpdateException($"GitHub respondió {(int)res.StatusCode}.");
            }

            var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return new ReleaseFetchResult(Parse(json), res.Headers.ETag?.ToString(), false);
        }
    }

    /// <summary>Convierte la respuesta de la API; ignora borradores y tags que no sean versiones.</summary>
    public static IReadOnlyList<ReleaseInfo> Parse(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new UpdateException("La respuesta de GitHub no se pudo leer.", ex);
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new UpdateException("La respuesta de GitHub no tiene el formato esperado.");
            }

            var list = new List<ReleaseInfo>();
            foreach (var r in doc.RootElement.EnumerateArray())
            {
                if (r.ValueKind != JsonValueKind.Object || Bool(r, "draft"))
                {
                    continue;
                }

                var tag = Str(r, "tag_name");
                var sem = SemVer.TryParse(tag);
                if (sem is null)
                {
                    continue;
                }

                DateTimeOffset? published = null;
                if (DateTimeOffset.TryParse(Str(r, "published_at"), CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal, out var p))
                {
                    published = p;
                }

                var assets = new List<ReleaseAsset>();
                if (r.TryGetProperty("assets", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var a in arr.EnumerateArray())
                    {
                        var name = Str(a, "name");
                        var url = Str(a, "browser_download_url");
                        if (name.Length == 0 || url.Length == 0)
                        {
                            continue;
                        }

                        var size = a.TryGetProperty("size", out var sz) && sz.TryGetInt64(out var n) ? n : 0;
                        var digest = Str(a, "digest");
                        string? sha = digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                            ? digest[7..].ToLowerInvariant()
                            : null;
                        assets.Add(new ReleaseAsset(name, url, size, sha));
                    }
                }

                var title = Str(r, "name");
                list.Add(new ReleaseInfo(
                    tag,
                    sem.ToString(),
                    title.Length > 0 ? title : $"HiveShock {sem}",
                    published,
                    Bool(r, "prerelease") || sem.IsPrerelease,
                    Str(r, "body"),
                    Str(r, "html_url"),
                    assets));
            }

            return list.OrderByDescending(static x => x.Semver).ToList();
        }
    }

    /// <summary>Lee un SHA256SUMS.txt («hash  nombre» por línea).</summary>
    public static string? FindHash(string sums, string assetName)
    {
        foreach (var line in sums.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 2 &&
                string.Equals(parts[1].TrimStart('*'), assetName, StringComparison.OrdinalIgnoreCase) &&
                parts[0].Length == 64)
            {
                return parts[0].ToLowerInvariant();
            }
        }

        return null;
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
