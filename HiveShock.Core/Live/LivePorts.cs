namespace HiveShock.Live;

public static class LivePortIds
{
    public const string TikTok = "tiktok";
    public const string Twitch = "twitch";
}

public enum LivePortStatus
{
    Off,
    Connecting,
    Live,
    Error,
    Ended,
}

[Flags]
public enum LivePortCapability
{
    None = 0,
    Chat = 1,
    Follow = 2,
    Share = 4,
    Likes = 8,
    Gifts = 16,
    Catalog = 32,
    Bits = 64,
    ChannelPoints = 128,
}

/// <summary>Un canal de live (TikTok, Twitch, …). Añadir otro = esta interfaz + tarjeta en Inicio.</summary>
public interface ILivePort
{
    string Id { get; }
    string DisplayName { get; }
    bool IsEnabled { get; }
    bool IsConfigured { get; }
    LivePortCapability Capabilities { get; }
    Task RunAsync(CancellationToken ct);
}

public sealed class LivePortSnapshot
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public LivePortStatus Status { get; init; }
    public string Message { get; init; } = "";
}

public sealed class LivePortHub
{
    private readonly object _gate = new();
    private readonly Dictionary<string, LivePortSnapshot> _ports = new(StringComparer.OrdinalIgnoreCase);

    public event Action? Changed;

    public LivePortHub()
    {
        Set(LivePortIds.TikTok, "TikTok", LivePortStatus.Off, "");
        Set(LivePortIds.Twitch, "Twitch", LivePortStatus.Off, "");
    }

    public void ResetSession()
    {
        Set(LivePortIds.TikTok, "TikTok", LivePortStatus.Off, "");
        Set(LivePortIds.Twitch, "Twitch", LivePortStatus.Off, "");
    }

    public void Set(string id, string displayName, LivePortStatus status, string message)
    {
        lock (_gate)
        {
            _ports[id] = new LivePortSnapshot
            {
                Id = id,
                DisplayName = displayName,
                Status = status,
                Message = message ?? "",
            };
        }

        Changed?.Invoke();
    }

    public LivePortSnapshot Get(string id)
    {
        lock (_gate)
        {
            return _ports.TryGetValue(id, out var snap)
                ? snap
                : new LivePortSnapshot { Id = id, DisplayName = id, Status = LivePortStatus.Off };
        }
    }

    public IReadOnlyList<LivePortSnapshot> All()
    {
        lock (_gate)
        {
            return _ports.Values.ToList();
        }
    }

    public bool AnyLive() => All().Any(p => p.Status == LivePortStatus.Live);

    public string StatusSummary()
    {
        var parts = All()
            .Where(p => p.Status is LivePortStatus.Live or LivePortStatus.Connecting or LivePortStatus.Error)
            .Select(p => p.Status switch
            {
                LivePortStatus.Live => $"{p.DisplayName} en vivo",
                LivePortStatus.Connecting => $"{p.DisplayName}…",
                LivePortStatus.Error => $"{p.DisplayName} error",
                _ => p.DisplayName,
            })
            .ToList();
        return parts.Count == 0 ? "" : string.Join(" · ", parts);
    }
}

/// <summary>
/// Mide cuánto duró un corte de conexión a un canal. Se avisa al usuario porque lo que se envió durante el corte
/// no se puede recuperar (TikTok y Twitch no reenvían ese tramo).
/// </summary>
public sealed class ConnectionGap
{
    public static readonly TimeSpan ReportAfter = TimeSpan.FromSeconds(5);

    private readonly Func<DateTimeOffset> _clock;
    private DateTimeOffset? _downSince;
    private bool _everUp;

    public ConnectionGap(Func<DateTimeOffset>? clock = null) => _clock = clock ?? (() => DateTimeOffset.UtcNow);

    /// <summary>La conexión se cayó o se detectó muerta (solo cuenta el primer aviso hasta que vuelva).</summary>
    public void NoteDown() => _downSince ??= _everUp ? _clock() : null;

    /// <summary>Se (re)conectó. Devuelve la duración del corte si fue largo y ya habíamos estado conectados antes.</summary>
    public TimeSpan? NoteUp()
    {
        var since = _downSince;
        _downSince = null;
        _everUp = true;
        if (since is null)
        {
            return null;
        }

        var gap = _clock() - since.Value;
        return gap >= ReportAfter ? gap : null;
    }

    public static string Describe(string channel, TimeSpan gap) =>
        $"Corte de {gap.TotalSeconds:0} s en {channel}: lo enviado en ese tramo no se puede recuperar; los repetidos se ignoran.";
}

/// <summary>
/// Reconoce los mensajes que el canal reenvía justo después de reconectar (anteriores a la reconexión).
/// Sirve para saber con datos reales si la reanudación por cursor de TikTok recupera algo.
/// </summary>
public sealed class ReplayProbe
{
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan MinAge = TimeSpan.FromSeconds(3);

    private readonly Func<DateTimeOffset> _clock;
    private DateTimeOffset? _connectedAt;
    private long _replayedGifts;
    private long _replayedChats;

    public ReplayProbe(Func<DateTimeOffset>? clock = null) => _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public long ReplayedGifts => Interlocked.Read(ref _replayedGifts);

    public long ReplayedChats => Interlocked.Read(ref _replayedChats);

    public void NoteConnected() => _connectedAt = _clock();

    /// <summary>True si el mensaje (hora de creación en ms o s) es de antes de reconectar y llegó en la ventana inicial.</summary>
    public bool IsReplay(long createTimeMs, bool isGift)
    {
        if (_connectedAt is not { } connected || createTimeMs <= 0)
        {
            return false;
        }

        var created = DateTimeOffset.FromUnixTimeMilliseconds(createTimeMs > 100_000_000_000 ? createTimeMs : createTimeMs * 1000);
        var now = _clock();
        if (now - connected > Window || connected - created < MinAge)
        {
            return false;
        }

        if (isGift)
        {
            Interlocked.Increment(ref _replayedGifts);
        }
        else
        {
            Interlocked.Increment(ref _replayedChats);
        }

        return true;
    }
}

/// <summary>
/// Backoff creciente para el bucle de reconexión de un canal: si el streamer
/// lleva horas sin empezar el live, no tiene sentido tocar la API cada 8s
/// indefinidamente. Vuelve al mínimo en cuanto se llega a "en vivo" una vez.
/// </summary>
public sealed class ReconnectBackoff
{
    private static readonly TimeSpan MinDelay = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(60);

    private TimeSpan _next = MinDelay;

    public TimeSpan NextDelay(bool reachedLive)
    {
        if (reachedLive)
        {
            _next = MinDelay;
            return MinDelay;
        }

        var current = _next;
        _next = TimeSpan.FromSeconds(Math.Min(MaxDelay.TotalSeconds, _next.TotalSeconds * 2));
        return current;
    }
}
