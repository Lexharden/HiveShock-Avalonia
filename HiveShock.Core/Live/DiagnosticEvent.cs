namespace HiveShock.Live;

/// <summary>
/// Representa un evento capturado en tiempo real para inspección y depuración.
/// Seguro tanto en Debug como en Release (en Release no se generan eventos).
/// </summary>
public sealed class DiagnosticEvent
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public string Source { get; init; } = ""; // "TikTok" o "Twitch"
    public string RawType { get; init; } = ""; // "WebcastGiftMessage", "Unknown(...)", etc.
    public string Category { get; init; } = ""; // "Gift", "Chat", "Like", "Follow", "Share", "System", "Unknown"
    public string Summary { get; init; } = ""; // Resumen legible
    public string Status { get; init; } = ""; // "gift", "chat", "like", "follow", "share", "system", "unknown"
    public string? Detail { get; init; } // Detalle técnico / raw hex si aplica
}
