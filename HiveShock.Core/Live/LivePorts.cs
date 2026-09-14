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
