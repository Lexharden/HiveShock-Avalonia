using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HiveShock.Logging;

namespace HiveShock.Voice;

/// <summary>
/// Cliente del motor neural "Read Aloud" de Microsoft Edge, por el mismo endpoint no
/// documentado que usa el proyecto open-source `edge-tts` (no existe SDK ni API pública
/// oficial de Microsoft para esto — ver la nota de arquitectura). TrustedClientToken es
/// una constante pública incrustada en el propio Edge, no una credencial de HiveShock.
/// Sec-MS-GEC es el mecanismo anti-abuso que Microsoft añadió después: un hash que
/// cualquier cliente puede calcular localmente a partir de la hora. Un 403 suele ser
/// reloj desfasado (se corrige con la hora del servidor y se reintenta una vez); si
/// sigue en 403, Microsoft cambió o cerró el acceso y se lanza
/// <see cref="SpeechServiceBlockedException"/> para que la UI bloquee la sección.
/// </summary>
public sealed class EdgeTtsSpeechSynthesizer : ISpeechSynthesizer
{
    private const string TrustedClientToken = "6A5AA1D4EAFF4E9FB37E23D68491D6F4";
    private const string WssUrl = "wss://speech.platform.bing.com/consumer/speech/synthesize/readaloud/edge/v1";
    private const string VoicesUrl = "https://speech.platform.bing.com/consumer/speech/synthesize/readaloud/voices/list";
    // Microsoft rechaza (403) versiones de Edge viejas: si vuelve el bloqueo, lo primero es
    // subir esto a la versión estable actual de Edge (la que use el proyecto edge-tts).
    private const string ChromiumVersion = "143.0.3650.75";
    private static readonly string ChromiumMajor = ChromiumVersion.Split('.')[0];
    private const string OutputFormat = "audio-24khz-48kbitrate-mono-mp3";
    private const string DefaultVoiceId = "es-MX-DaliaNeural";

    private static readonly TimeSpan SynthesisTimeout = TimeSpan.FromSeconds(20);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>Segundos a sumar al reloj local para coincidir con el de Microsoft (lo aprende de un 403).</summary>
    private static double _clockSkewSeconds;

    private const string BlockedMessage =
        "Microsoft rechazó la conexión con su servicio de voces (código 403).";

    public async Task<Stream> SynthesizeAsync(string text, VoiceProfile voice, SpeechStyle style, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(SynthesisTimeout);
        try
        {
            using var socket = await ConnectAsync(timeout.Token).ConfigureAwait(false);
            return await SynthesizeOnSocketAsync(socket, text, voice, style, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("El servicio de voces tardó demasiado en responder.");
        }
    }

    public async Task<IReadOnlyList<VoiceProfile>> ListVoicesAsync(CancellationToken ct)
    {
        var url = $"{VoicesUrl}?trustedclienttoken={TrustedClientToken}";
        using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new SpeechServiceBlockedException(BlockedMessage);
        }

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        var list = new List<VoiceProfile>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var shortName = el.TryGetProperty("ShortName", out var sn) ? sn.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(shortName))
            {
                continue;
            }

            var locale = el.TryGetProperty("Locale", out var loc) ? loc.GetString() ?? "" : "";
            list.Add(new VoiceProfile(shortName, FriendlyVoiceName(shortName, locale), locale));
        }

        return list
            .OrderBy(v => v.Locale.StartsWith("es-", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(v => v.Locale, StringComparer.OrdinalIgnoreCase)
            .ThenBy(v => v.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>"es-MX-DaliaNeural" → "Dalia"; "en-US-AndrewMultilingualNeural" → "Andrew Multilingual".</summary>
    private static string FriendlyVoiceName(string shortName, string locale)
    {
        var name = shortName;
        if (!string.IsNullOrEmpty(locale) && name.StartsWith(locale + "-", StringComparison.OrdinalIgnoreCase))
        {
            name = name[(locale.Length + 1)..];
        }

        if (name.EndsWith("Neural", StringComparison.Ordinal))
        {
            name = name[..^"Neural".Length];
        }

        name = name.Replace("Multilingual", " Multilingual", StringComparison.Ordinal).Trim();
        return name.Length == 0 ? shortName : name;
    }

    private static async Task<ClientWebSocket> ConnectAsync(CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var connectionId = Guid.NewGuid().ToString("N");
            // Sec-MS-GEC va en la URL (en cabecera Microsoft responde 403).
            var url = $"{WssUrl}?TrustedClientToken={TrustedClientToken}&ConnectionId={connectionId}" +
                      $"&Sec-MS-GEC={ComputeSecMsGec()}&Sec-MS-GEC-Version=1-{ChromiumVersion}";
            var socket = new ClientWebSocket();
            socket.Options.CollectHttpResponseDetails = true;
            socket.Options.SetRequestHeader(
                "User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
                $"Chrome/{ChromiumMajor}.0.0.0 Safari/537.36 Edg/{ChromiumMajor}.0.0.0");
            socket.Options.SetRequestHeader("Origin", "chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold");
            socket.Options.SetRequestHeader("Pragma", "no-cache");
            socket.Options.SetRequestHeader("Cache-Control", "no-cache");
            socket.Options.SetRequestHeader("Accept-Language", "en-US,en;q=0.9");
            socket.Options.SetRequestHeader("Cookie", $"muid={Convert.ToHexString(RandomNumberGenerator.GetBytes(16))};");
            try
            {
                await socket.ConnectAsync(new Uri(url), ct).ConfigureAwait(false);
                return socket;
            }
            catch (WebSocketException) when (socket.HttpStatusCode == HttpStatusCode.Forbidden)
            {
                var serverDate = ReadServerDate(socket);
                socket.Dispose();
                if (attempt == 0 && serverDate is { } date)
                {
                    // Sec-MS-GEC depende de la hora: con el reloj de Windows desfasado Microsoft
                    // responde 403 aunque el servicio funcione. Se ajusta y se reintenta una vez.
                    _clockSkewSeconds = (date - DateTimeOffset.UtcNow).TotalSeconds;
                    BridgeLog.Warn($"Voz: reloj desfasado {_clockSkewSeconds:0}s respecto a Microsoft, reintentando.");
                    continue;
                }

                throw new SpeechServiceBlockedException(BlockedMessage);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    }

    private static DateTimeOffset? ReadServerDate(ClientWebSocket socket)
    {
        if (socket.HttpResponseHeaders is not { } headers)
        {
            return null;
        }

        foreach (var (key, values) in headers)
        {
            if (string.Equals(key, "Date", StringComparison.OrdinalIgnoreCase) &&
                DateTimeOffset.TryParse(values.FirstOrDefault(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var date))
            {
                return date;
            }
        }

        return null;
    }

    private static async Task<Stream> SynthesizeOnSocketAsync(
        ClientWebSocket socket,
        string text,
        VoiceProfile voice,
        SpeechStyle style,
        CancellationToken ct)
    {
        var voiceId = string.IsNullOrWhiteSpace(voice.Id) ? DefaultVoiceId : voice.Id;
        var requestId = Guid.NewGuid().ToString("N");
        var timestamp = DateTime.UtcNow.ToString("ddd MMM dd yyyy HH:mm:ss", CultureInfo.InvariantCulture) +
                        " GMT+0000 (Coordinated Universal Time)";

        var config =
            "X-Timestamp:" + timestamp + "\r\n" +
            "Content-Type:application/json; charset=utf-8\r\n" +
            "Path:speech.config\r\n\r\n" +
            "{\"context\":{\"synthesis\":{\"audio\":{\"metadataoptions\":{" +
            "\"sentenceBoundaryEnabled\":\"false\",\"wordBoundaryEnabled\":\"false\"}," +
            "\"outputFormat\":\"" + OutputFormat + "\"}}}}";
        await SendTextAsync(socket, config, ct).ConfigureAwait(false);

        var rate = Math.Clamp(style.RatePercent, -50, 100);
        var pitch = Math.Clamp(style.PitchPercent, -50, 50);
        var ssml =
            "<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='en-US'>" +
            $"<voice name='{EscapeSsml(voiceId)}'>" +
            $"<prosody pitch='{pitch:+0;-0;+0}Hz' rate='{rate:+0;-0;+0}%' volume='+0%'>" +
            EscapeSsml(text) +
            "</prosody></voice></speak>";
        var ssmlMessage =
            "X-RequestId:" + requestId + "\r\n" +
            "Content-Type:application/ssml+xml\r\n" +
            "X-Timestamp:" + timestamp + "Z\r\n" +
            "Path:ssml\r\n\r\n" +
            ssml;
        await SendTextAsync(socket, ssmlMessage, ct).ConfigureAwait(false);

        var audio = new MemoryStream();
        var buffer = new byte[16 * 1024];

        while (true)
        {
            using var frame = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return Finish(audio);
                }

                frame.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            var data = frame.ToArray();
            if (result.MessageType == WebSocketMessageType.Binary)
            {
                if (data.Length < 2)
                {
                    continue;
                }

                var headerLength = (data[0] << 8) | data[1];
                var audioStart = 2 + headerLength;
                if (headerLength <= 0 || audioStart > data.Length)
                {
                    continue;
                }

                audio.Write(data, audioStart, data.Length - audioStart);
            }
            else if (Encoding.UTF8.GetString(data).Contains("Path:turn.end", StringComparison.Ordinal))
            {
                break;
            }
        }

        try
        {
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch
        {
            // el servidor puede haber cerrado ya la conexión
        }

        return Finish(audio);
    }

    private static Stream Finish(MemoryStream audio)
    {
        if (audio.Length == 0)
        {
            throw new InvalidOperationException("El servicio de voces no devolvió audio. Prueba con otra voz.");
        }

        audio.Position = 0;
        return audio;
    }

    private static async Task SendTextAsync(ClientWebSocket socket, string message, CancellationToken ct) =>
        await socket.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, true, ct)
            .ConfigureAwait(false);

    private static string EscapeSsml(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("'", "&apos;").Replace("\"", "&quot;");

    /// <summary>SHA-256 de (hora UTC corregida, redondeada a 5 min, en ticks de Windows FILETIME) + TrustedClientToken.</summary>
    private static string ComputeSecMsGec()
    {
        var unixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + (long)_clockSkewSeconds;
        var windowsEpochSeconds = unixSeconds + 11644473600L;
        var rounded = windowsEpochSeconds - (windowsEpochSeconds % 300);
        var ticks = rounded * 10_000_000L;
        var toHash = ticks.ToString(CultureInfo.InvariantCulture) + TrustedClientToken;
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(toHash));
        return Convert.ToHexString(hash);
    }
}
