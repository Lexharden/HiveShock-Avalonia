using System.Globalization;
using System.Net.Http;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HiveShock.Voice;

/// <summary>
/// Cliente del motor neural "Read Aloud" de Microsoft Edge, por el mismo endpoint no
/// documentado que usa el proyecto open-source `edge-tts` (no existe SDK ni API pública
/// oficial de Microsoft para esto — ver la nota de arquitectura). TrustedClientToken es
/// una constante pública incrustada en el propio Edge, no una credencial de HiveShock.
/// Sec-MS-GEC es el mecanismo anti-abuso que Microsoft añadió después: un hash que
/// cualquier cliente puede calcular localmente a partir de la hora. Si Microsoft cambia
/// ese cálculo (ha pasado antes), esto empezará a fallar con 403 hasta actualizarlo.
/// </summary>
public sealed class EdgeTtsSpeechSynthesizer : ISpeechSynthesizer
{
    private const string TrustedClientToken = "6A5AA1D4EAFF4E9FB37E23D68491D6F4";
    private const string WssUrl = "wss://speech.platform.bing.com/consumer/speech/synthesize/readaloud/edge/v1";
    private const string VoicesUrl = "https://speech.platform.bing.com/consumer/speech/synthesize/readaloud/voices/list";
    private const string ChromiumVersion = "130.0.2849.68";
    private const string OutputFormat = "audio-24khz-48kbitrate-mono-mp3";
    private const string DefaultVoiceId = "en-US-AndrewNeural";

    public async Task<Stream> SynthesizeAsync(string text, VoiceProfile voice, CancellationToken ct)
    {
        var voiceId = string.IsNullOrWhiteSpace(voice.Id) ? DefaultVoiceId : voice.Id;
        var connectionId = Guid.NewGuid().ToString("N");
        var url = $"{WssUrl}?TrustedClientToken={TrustedClientToken}&ConnectionId={connectionId}";

        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Sec-MS-GEC", ComputeSecMsGec());
        socket.Options.SetRequestHeader("Sec-MS-GEC-Version", $"1-{ChromiumVersion}");
        socket.Options.SetRequestHeader(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
            $"Chrome/{ChromiumVersion} Safari/537.36 Edg/{ChromiumVersion}");
        socket.Options.SetRequestHeader("Origin", "chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold");
        await socket.ConnectAsync(new Uri(url), ct).ConfigureAwait(false);

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

        var ssml =
            "<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='en-US'>" +
            $"<voice name='{voiceId}'>" +
            "<prosody pitch='+0Hz' rate='+0%' volume='+0%'>" +
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
                    audio.Position = 0;
                    return audio;
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

        audio.Position = 0;
        return audio;
    }

    public async Task<IReadOnlyList<VoiceProfile>> ListVoicesAsync(CancellationToken ct)
    {
        using var http = new HttpClient();
        var url = $"{VoicesUrl}?trustedclienttoken={TrustedClientToken}";
        var json = await http.GetStringAsync(url, ct).ConfigureAwait(false);
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
            var friendly = el.TryGetProperty("FriendlyName", out var fn) ? fn.GetString() ?? shortName : shortName;
            list.Add(new VoiceProfile(shortName, friendly, locale));
        }

        return list;
    }

    private static async Task SendTextAsync(ClientWebSocket socket, string message, CancellationToken ct) =>
        await socket.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, true, ct)
            .ConfigureAwait(false);

    private static string EscapeSsml(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    /// <summary>SHA-256 de (hora UTC redondeada a 5 min, en ticks de Windows FILETIME) + TrustedClientToken.</summary>
    private static string ComputeSecMsGec()
    {
        var unixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var windowsEpochSeconds = unixSeconds + 11644473600L;
        var rounded = windowsEpochSeconds - (windowsEpochSeconds % 300);
        var ticks = rounded * 10_000_000L;
        var toHash = ticks.ToString(CultureInfo.InvariantCulture) + TrustedClientToken;
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(toHash));
        return Convert.ToHexString(hash);
    }
}
