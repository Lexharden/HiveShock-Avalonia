using TikTokLive.Events;
using TikTokLive.Proto;

namespace HiveShock.Live;

/// <summary>
/// Sumidero de diagnóstico para capturar y examinar eventos raw de TikTok/Twitch en vivo.
/// En compilación DEBUG: mantiene un buffer circular de 500 eventos con estadísticas y filtros.
/// En compilación RELEASE: es un no-op sin consumo de recursos.
/// </summary>
public sealed class LiveDiagnosticSink
{
#if DEBUG
    private const int MaxEvents = 500;
    private readonly object _gate = new();
    private readonly List<DiagnosticEvent> _events = new(MaxEvents + 1);
    private int _dropped;

    public event Action? Changed;

    public void Record(TikTokLiveEvent evt, string source = "TikTok")
    {
        var diag = ToDiagnostic(evt, source);
        lock (_gate)
        {
            _events.Insert(0, diag); // Más recientes primero
            if (_events.Count > MaxEvents)
            {
                _events.RemoveAt(_events.Count - 1);
                _dropped++;
            }
        }
        Changed?.Invoke();
    }

    public (IReadOnlyList<DiagnosticEvent> events, int dropped) Snapshot()
    {
        lock (_gate)
        {
            return (_events.ToList(), _dropped);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _events.Clear();
            _dropped = 0;
        }
        Changed?.Invoke();
    }

    private static DiagnosticEvent ToDiagnostic(TikTokLiveEvent evt, string source)
    {
        var (category, summary, status, detail) = Describe(evt);
        return new DiagnosticEvent
        {
            Timestamp = DateTime.Now,
            Source = source,
            RawType = EventTypeName(evt),
            Category = category,
            Summary = summary,
            Status = status,
            Detail = detail,
        };
    }

    private static string EventTypeName(TikTokLiveEvent evt)
    {
        if (evt.Type == TikTokLiveEventType.Unknown && evt.Data is UnknownEvent unk)
            return $"Unknown({unk.Method})";
        return evt.Type.ToString();
    }

    private static (string category, string summary, string status, string? detail)
        Describe(TikTokLiveEvent evt)
    {
        switch (evt.Type)
        {
            case TikTokLiveEventType.Gift:
            {
                var g = evt.As<WebcastGiftMessage>();
                var name = g.GiftDetails?.GiftName ?? "";
                var id = g.GiftId != 0
                    ? g.GiftId.ToString()
                    : g.GiftDetails?.Id is > 0 ? g.GiftDetails.Id.ToString() : "?";
                var diamonds = g.GiftDetails?.DiamondCount ?? 0;
                var user = g.User?.Nickname ?? g.User?.UniqueId ?? "?";
                var label = !string.IsNullOrWhiteSpace(name) ? name : $"id {id}";
                var summary = $"🎁 {label} x{Math.Max(1, g.RepeatCount)} ({diamonds}d) — {user}";
                var detail = $"GiftId={id} GroupId={g.GroupId} RepeatEnd={g.RepeatEnd} Combo={g.IsComboGift()}";
                return ("Gift", summary, "gift", detail);
            }
            case TikTokLiveEventType.Chat:
            {
                var c = evt.As<WebcastChatMessage>();
                var user = c.User?.Nickname ?? c.User?.UniqueId ?? "?";
                var text = c.Comment?.Length > 80 ? c.Comment[..80] + "…" : c.Comment ?? "";
                return ("Chat", $"💬 {user}: {text}", "chat", null);
            }
            case TikTokLiveEventType.Like:
            {
                var l = evt.As<WebcastLikeMessage>();
                var user = l.User?.Nickname ?? l.User?.UniqueId ?? "?";
                return ("Like", $"❤️ {user} x{l.LikeCount}", "like", null);
            }
            case TikTokLiveEventType.Follow:
            {
                var s = evt.As<WebcastSocialMessage>();
                var user = s.User?.Nickname ?? s.User?.UniqueId ?? "?";
                return ("Follow", $"➕ Follow: {user}", "follow", null);
            }
            case TikTokLiveEventType.Share:
            {
                var s = evt.As<WebcastSocialMessage>();
                var user = s.User?.Nickname ?? s.User?.UniqueId ?? "?";
                return ("Share", $"📤 Share: {user}", "share", null);
            }
            case TikTokLiveEventType.Join:
            {
                var m = evt.As<WebcastMemberMessage>();
                return ("Join", $"👋 Join (action={m.Action})", "system", null);
            }
            case TikTokLiveEventType.Connected:
                return ("System", $"✅ Conectado room={evt.AsRoomId()}", "system", null);
            case TikTokLiveEventType.Disconnected:
                return ("System", "🔌 Desconectado", "system", null);
            case TikTokLiveEventType.Reconnecting:
            {
                var r = evt.As<ReconnectInfo>();
                return ("System", $"🔄 Reconectando {r.Attempt}/{r.MaxRetries} (delay {r.DelaySecs}s)", "system", null);
            }
            case TikTokLiveEventType.LiveEnded:
                return ("System", "🏁 Live terminado", "system", null);
            case TikTokLiveEventType.Barrage:
            {
                var b = evt.As<WebcastBarrageMessage>();
                var status = b.GalleryGiftId > 0 ? "gift" : "system";
                return ("Barrage", $"📺 Barrage MsgType={b.MsgType} GalleryGiftId={b.GalleryGiftId} SubType={b.SubType}", status, null);
            }
            case TikTokLiveEventType.RoomUserSeq:
            {
                var r = evt.As<WebcastRoomUserSeqMessage>();
                return ("System", $"👥 Viewers: {r.ViewerCount}", "system", null);
            }
            case TikTokLiveEventType.Unknown:
            {
                var u = evt.As<UnknownEvent>();
                var prefix = BitConverter.ToString(u.Payload.Take(32).ToArray());
                var suffix = u.Payload.Length > 32 ? "..." : "";
                return ("Unknown", $"❓ Unknown: {u.Method} ({u.Payload.Length} bytes)", "unknown", $"Bytes: {prefix}{suffix}");
            }
            default:
                return ("System", $"ℹ️ {evt.Type}", "system", null);
        }
    }
#else
    public event Action? Changed { add { } remove { } }
    public void Record(TikTokLiveEvent evt, string source = "TikTok") { }
    public (IReadOnlyList<DiagnosticEvent> events, int dropped) Snapshot() => (Array.Empty<DiagnosticEvent>(), 0);
    public void Clear() { }
#endif
}
