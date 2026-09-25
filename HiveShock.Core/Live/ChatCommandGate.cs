namespace HiveShock.Live;

public enum ChatAdmitKind
{
    Ok,
    Cooldown,
    GlobalGap,
    QueueFull,
}

public readonly record struct ChatAdmitResult(ChatAdmitKind Kind, int RetrySec, bool Log)
{
    public bool Allowed => Kind == ChatAdmitKind.Ok;

    public static ChatAdmitResult Ok() => new(ChatAdmitKind.Ok, 0, false);
}

/// <summary>
/// Anti-spam de comandos de chat: cooldown por viewer, hueco global y tope de cola.
/// Memoria del directo; no se guarda en disco.
/// </summary>
public sealed class ChatCommandGate
{
    public const int DefaultCooldownSec = 30;
    public const int DefaultGlobalGapSec = 2;
    public const int MaxPendingChat = 8;
    public const int MaxSec = 3600;

    private const int MaxTracked = 4096;
    private static readonly long SweepEveryTicks = TimeSpan.FromSeconds(45).Ticks;
    private static readonly long GlobalLogGapTicks = TimeSpan.FromSeconds(8).Ticks;

    private readonly object _gate = new();
    private readonly Dictionary<string, ViewerState> _viewers = new(StringComparer.OrdinalIgnoreCase);
    private long _lastGlobalFireTicks;
    private long _lastCrowdLogTicks;
    private long _lastSweepTicks;

    public ChatAdmitResult TryAdmit(string key, int cooldownSec, int globalGapSec, int pendingChat)
    {
        cooldownSec = ClampSec(cooldownSec);
        globalGapSec = ClampSec(globalGapSec);
        var now = DateTime.UtcNow.Ticks;

        lock (_gate)
        {
            SweepIfNeeded(now, cooldownSec, globalGapSec);

            if (pendingChat >= MaxPendingChat)
            {
                return Crowd(now, ChatAdmitKind.QueueFull, 1);
            }

            if (globalGapSec > 0 && _lastGlobalFireTicks > 0)
            {
                var readyAt = _lastGlobalFireTicks + TimeSpan.FromSeconds(globalGapSec).Ticks;
                if (now < readyAt)
                {
                    var retry = SecondsLeft(readyAt, now);
                    return Crowd(now, ChatAdmitKind.GlobalGap, retry);
                }
            }

            if (!_viewers.TryGetValue(key, out var state))
            {
                state = new ViewerState();
                _viewers[key] = state;
            }

            state.LastSeenTicks = now;

            if (cooldownSec > 0 && state.NextAllowedTicks > now)
            {
                var retry = SecondsLeft(state.NextAllowedTicks, now);
                var log = !state.DenyLogged;
                state.DenyLogged = true;
                return new ChatAdmitResult(ChatAdmitKind.Cooldown, retry, log);
            }

            state.NextAllowedTicks = cooldownSec > 0
                ? now + TimeSpan.FromSeconds(cooldownSec).Ticks
                : now;
            state.DenyLogged = false;
            _lastGlobalFireTicks = now;
            return ChatAdmitResult.Ok();
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _viewers.Clear();
            _lastGlobalFireTicks = 0;
            _lastCrowdLogTicks = 0;
            _lastSweepTicks = 0;
        }
    }

    public static int ClampSec(int value) => Math.Clamp(value, 0, MaxSec);

    public static string ViewerKey(string portId, string? stableId)
    {
        var id = string.IsNullOrWhiteSpace(stableId) ? "anon" : stableId.Trim();
        return $"{portId}:{id}";
    }

    private ChatAdmitResult Crowd(long now, ChatAdmitKind kind, int retrySec)
    {
        var log = now - _lastCrowdLogTicks >= GlobalLogGapTicks;
        if (log)
        {
            _lastCrowdLogTicks = now;
        }

        return new ChatAdmitResult(kind, Math.Max(1, retrySec), log);
    }

    private void SweepIfNeeded(long now, int cooldownSec, int globalGapSec)
    {
        if (_viewers.Count == 0)
        {
            return;
        }

        if (_viewers.Count < MaxTracked && now - _lastSweepTicks < SweepEveryTicks)
        {
            return;
        }

        _lastSweepTicks = now;
        var keepTicks = TimeSpan.FromSeconds(Math.Max(60, Math.Max(cooldownSec, globalGapSec) * 2)).Ticks;
        foreach (var id in _viewers.Keys.ToList())
        {
            if (now - _viewers[id].LastSeenTicks >= keepTicks)
            {
                _viewers.Remove(id);
            }
        }

        if (_viewers.Count <= MaxTracked)
        {
            return;
        }

        foreach (var id in _viewers.OrderBy(kv => kv.Value.LastSeenTicks).Take(_viewers.Count - MaxTracked / 2)
                     .Select(kv => kv.Key).ToList())
        {
            _viewers.Remove(id);
        }
    }

    private static int SecondsLeft(long readyAt, long now) =>
        Math.Max(1, (int)Math.Ceiling(TimeSpan.FromTicks(readyAt - now).TotalSeconds));

    private sealed class ViewerState
    {
        public long NextAllowedTicks;
        public long LastSeenTicks;
        public bool DenyLogged;
    }
}
