using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HiveShock.Zeldathon;

public enum ZeldathonInboundKind
{
    Unknown,
    Ack,
    Clock,
    Error,
    ForceClose,
}

public sealed record ZeldathonInbound(
    ZeldathonInboundKind Kind,
    string? Id = null,
    ZeldathonClockSnapshot? Clock = null,
    string Code = "",
    string Message = "");

/// <summary>Un mensaje hacia el servidor. <see cref="Json"/> ya lleva <c>type</c> e <c>id</c>.</summary>
public sealed record ZeldathonOutbound(string Type, string Id, string Json);

/// <summary>Contrato de ingesta (docs/hiveshock-ingest.md del backend): construcción y lectura de mensajes.</summary>
public static class ZeldathonProtocol
{
    public const int MaxTextLength = 64;

    public static ZeldathonOutbound Message(string type, string? id = null, JsonObject? fields = null)
    {
        id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id;
        var node = new JsonObject { ["type"] = type, ["id"] = id };
        if (fields != null)
        {
            foreach (var (key, value) in fields)
            {
                node[key] = value?.DeepClone();
            }
        }

        return new ZeldathonOutbound(type, id, node.ToJsonString());
    }

    public static ZeldathonOutbound Hello(string clientVersion) =>
        Message("HELLO", fields: new JsonObject { ["clientVersion"] = clientVersion });

    public static ZeldathonOutbound Heartbeat(string id, bool? gameRunning) =>
        Message("HEARTBEAT", id, gameRunning is { } running ? new JsonObject { ["gameRunning"] = running } : null);

    public static ZeldathonInbound Parse(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new ZeldathonInbound(ZeldathonInboundKind.Unknown);
            }

            var type = Str(root, "type");
            var id = root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                ? idEl.GetString()
                : null;
            switch (type)
            {
                case "ACK":
                    return new ZeldathonInbound(ZeldathonInboundKind.Ack, id);
                case "GAME_FORCE_CLOSE":
                    return new ZeldathonInbound(ZeldathonInboundKind.ForceClose, id);
                case "ERROR":
                    return new ZeldathonInbound(ZeldathonInboundKind.Error, id, null, Str(root, "code"), Str(root, "message"));
                case "CLOCK" when root.TryGetProperty("clock", out var clock) && TryClock(clock, out var snap):
                    return new ZeldathonInbound(ZeldathonInboundKind.Clock, id, snap);
                default:
                    return new ZeldathonInbound(ZeldathonInboundKind.Unknown, id);
            }
        }
        catch (JsonException)
        {
            return new ZeldathonInbound(ZeldathonInboundKind.Unknown);
        }
    }

    public static ZeldathonRacerStatus ParseStatus(string? status) => status switch
    {
        "online" => ZeldathonRacerStatus.Online,
        "live" => ZeldathonRacerStatus.Live,
        "paused" => ZeldathonRacerStatus.Paused,
        "offline" => ZeldathonRacerStatus.Offline,
        "exhausted" => ZeldathonRacerStatus.Exhausted,
        "finished" => ZeldathonRacerStatus.Finished,
        _ => ZeldathonRacerStatus.Unknown,
    };

    /// <summary>Mensaje legible para el usuario a partir de un código de error del servidor.</summary>
    public static string Explain(string code, string message) => code switch
    {
        "event_not_live" => "El evento todavía no está en vivo.",
        "out_of_sequence" => "El servidor recibió un aviso fuera de orden.",
        "requirements_not_met" => "Aún faltan objetivos requeridos para terminar el juego.",
        "exhausted" => "Se acabó el tiempo de hoy.",
        "replaced" => "Otra conexión con tu token tomó el lugar de esta.",
        "unknown_racer" => "El token no corresponde a ningún corredor.",
        "invalid" => string.IsNullOrWhiteSpace(message) ? "El servidor rechazó un dato." : message,
        _ => string.IsNullOrWhiteSpace(message) ? code : message,
    };

    private static bool TryClock(JsonElement el, out ZeldathonClockSnapshot snapshot)
    {
        snapshot = null!;
        if (!el.TryGetProperty("remainingMs", out var rem) || !rem.TryGetInt64(out var remainingMs))
        {
            return false;
        }

        snapshot = new ZeldathonClockSnapshot(
            Str(el, "racerId"),
            remainingMs,
            ParseStatus(Str(el, "status")),
            Date(Str(el, "resetAtUtc")),
            Date(Str(el, "serverTimeUtc")));
        return true;
    }

    private static string Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static DateTime Date(string text) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d)
            ? d
            : DateTime.MinValue;
}
