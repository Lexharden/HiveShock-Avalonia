using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TikTokLive.Errors;
using TikTokLive.Http;

namespace TikTokLive.Auth
{
    internal static class TtwidAuth
    {
        private static readonly Uri TikTokHome = new Uri("https://www.tiktok.com/");
        private static readonly Uri TikTokLiveHome = new Uri("https://www.tiktok.com/live");
        private static readonly Uri RegisterUrl = new Uri("https://ttwid.bytedance.com/ttwid/union/register/");

        private const string RegisterBody =
            "{\"region\":\"us\",\"aid\":1988,\"needFid\":false,\"service\":\"www.tiktok.com\"," +
            "\"migrate_info\":{\"ticket\":\"\",\"source\":\"node\"},\"cbUrlProtocol\":\"https\",\"union\":true}";

        /// <summary>
        /// Optional override (e.g. from env). When set, skips network fetch.
        /// </summary>
        public static string? OverrideTtwid { get; set; }

        public static async Task<string> FetchTtwidAsync(
            TimeSpan timeout, string? userAgent = null, IWebProxy? proxy = null,
            CancellationToken ct = default)
        {
            if (!string.IsNullOrWhiteSpace(OverrideTtwid))
                return OverrideTtwid!.Trim();

            Exception? last = null;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                string ua = userAgent ?? UserAgent.RandomUa();
                if (attempt > 0)
                    ua = UserAgent.RandomUa();

                try
                {
                    string? ttwid =
                        await TryFromHomepageAsync(TikTokHome, timeout, ua, proxy, followRedirects: true, ct)
                            .ConfigureAwait(false)
                        ?? await TryFromHomepageAsync(TikTokHome, timeout, ua, proxy, followRedirects: false, ct)
                            .ConfigureAwait(false)
                        ?? await TryFromHomepageAsync(TikTokLiveHome, timeout, ua, proxy, followRedirects: true, ct)
                            .ConfigureAwait(false)
                        ?? await TryFromRegisterAsync(timeout, ua, proxy, ct).ConfigureAwait(false);

                    if (!string.IsNullOrWhiteSpace(ttwid))
                        return ttwid!;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    last = ex;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)), ct).ConfigureAwait(false);
            }

            string detail = last != null ? $" ({last.Message})" : "";
            throw new TikTokLiveException(
                "no ttwid cookie in tiktok.com response" + detail +
                ". Pon TIKTOK_TTWID en .env (cópialo de las cookies del navegador en tiktok.com).");
        }

        private static async Task<string?> TryFromHomepageAsync(
            Uri url, TimeSpan timeout, string ua, IWebProxy? proxy, bool followRedirects,
            CancellationToken ct)
        {
            var jar = new CookieContainer();
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = followRedirects,
                UseCookies = true,
                CookieContainer = jar,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            };
            if (proxy != null)
            {
                handler.Proxy = proxy;
                handler.UseProxy = true;
            }

            using (handler)
            using (var client = CreateClient(handler, timeout, ua))
            using (var response = await client.GetAsync(url, ct).ConfigureAwait(false))
            {
                string? fromJar = ReadTtwidFromJar(jar, url)
                    ?? ReadTtwidFromJar(jar, TikTokHome)
                    ?? ReadTtwidFromJar(jar, new Uri("https://tiktok.com/"));
                if (fromJar != null)
                    return fromJar;

                if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
                {
                    foreach (string cookie in cookies)
                    {
                        string? ttwid = ExtractTtwid(cookie);
                        if (ttwid != null)
                            return ttwid;
                    }
                }

                // Some runtimes fold Set-Cookie into a single comma-joined header.
                if (response.Headers.TryGetValues("Set-Cookie", out var rawJoined))
                {
                    foreach (string blob in rawJoined)
                    {
                        string? ttwid = ExtractTtwidLoose(blob);
                        if (ttwid != null)
                            return ttwid;
                    }
                }

                return null;
            }
        }

        private static async Task<string?> TryFromRegisterAsync(
            TimeSpan timeout, string ua, IWebProxy? proxy, CancellationToken ct)
        {
            var jar = new CookieContainer();
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                UseCookies = true,
                CookieContainer = jar,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            };
            if (proxy != null)
            {
                handler.Proxy = proxy;
                handler.UseProxy = true;
            }

            using (handler)
            using (var client = CreateClient(handler, timeout, ua))
            using (var content = new StringContent(RegisterBody, Encoding.UTF8, "application/json"))
            using (var response = await client.PostAsync(RegisterUrl, content, ct).ConfigureAwait(false))
            {
                string? fromJar = ReadTtwidFromJar(jar, RegisterUrl)
                    ?? ReadTtwidFromJar(jar, TikTokHome)
                    ?? ReadTtwidFromJar(jar, new Uri("https://bytedance.com/"));
                if (fromJar != null)
                    return fromJar;

                if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
                {
                    foreach (string cookie in cookies)
                    {
                        string? ttwid = ExtractTtwid(cookie);
                        if (ttwid != null)
                            return ttwid;
                    }
                }

                return null;
            }
        }

        private static HttpClient CreateClient(HttpClientHandler handler, TimeSpan timeout, string ua)
        {
            var client = new HttpClient(handler) { Timeout = timeout };
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", ua);
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate, br");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Connection", "keep-alive");
            return client;
        }

        private static string? ReadTtwidFromJar(CookieContainer jar, Uri uri)
        {
            try
            {
                CookieCollection cookies = jar.GetCookies(uri);
                foreach (Cookie cookie in cookies)
                {
                    if (string.Equals(cookie.Name, "ttwid", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(cookie.Value))
                    {
                        return cookie.Value;
                    }
                }
            }
            catch
            {
                // ignore malformed jar entries
            }

            return null;
        }

        private static string? ExtractTtwid(string setCookieHeader)
        {
            string trimmed = setCookieHeader.TrimStart();
            if (!trimmed.StartsWith("ttwid=", StringComparison.OrdinalIgnoreCase))
                return null;

            int eq = trimmed.IndexOf('=');
            string value = trimmed.Substring(eq + 1);
            int end = value.IndexOf(';');
            if (end >= 0)
                value = value.Substring(0, end);

            value = value.Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }

        private static string? ExtractTtwidLoose(string headerBlob)
        {
            const string marker = "ttwid=";
            int idx = headerBlob.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return null;

            string value = headerBlob.Substring(idx + marker.Length);
            int end = value.IndexOfAny(new[] { ';', ',', ' ' });
            if (end >= 0)
                value = value.Substring(0, end);

            value = value.Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }
    }
}
