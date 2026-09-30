using System.Text.Json;
using System.Text.Json.Nodes;

namespace HiveShock.Zeldathon;

/// <summary>
/// Traduce lo que pasa en el juego (eventos por el puerto 43002) a mensajes de ingesta de Zeldathon.
/// Guarda el estado del juego y, en cada <see cref="Reconcile"/>, envía solo lo que el servidor aún no
/// tiene. Así el orden nunca importa (el servidor rechaza progreso si la sesión no está iniciada: se
/// espera a que lo esté) y una reconexión o un rechazo se arreglan solos reenviando la diferencia.
/// </summary>
public sealed class ZeldathonSession
{
    private static readonly TimeSpan StartRetry = TimeSpan.FromSeconds(10);

    private readonly object _gate = new();
    private readonly ZeldathonClock _clock;
    private readonly Action<ZeldathonOutbound> _send;
    private readonly Action? _requestClock;
    private readonly Func<long> _nowMs;

    // Lo que el juego dice que pasa.
    private bool _gameActive;
    private string _runId = Guid.NewGuid().ToString("N")[..8];
    private string? _area;
    private readonly HashSet<string> _items = new(StringComparer.Ordinal);
    private readonly HashSet<string> _objectives = new(StringComparer.Ordinal);
    private readonly HashSet<string> _bosses = new(StringComparer.OrdinalIgnoreCase);
    private double? _hearts;
    private double? _maxHearts;
    private long? _rupees;
    private long? _skulltulas;
    private bool _finishReady;

    // Lo que ya se le envió al servidor.
    private string? _sentArea;
    private readonly HashSet<string> _sentItems = new(StringComparer.Ordinal);
    private readonly HashSet<string> _sentBosses = new(StringComparer.OrdinalIgnoreCase);
    private string _sentProgress = "";
    private string _sentStats = "";
    private bool _sentFinished;
    private bool _startedSent;
    private long _lastStartMs = long.MinValue / 2;
    private int _startAttempts;

    public ZeldathonSession(
        ZeldathonClock clock,
        Action<ZeldathonOutbound> send,
        Action? requestClock = null,
        Func<long>? nowMs = null,
        ZeldathonMap? map = null)
    {
        _clock = clock;
        _send = send;
        _requestClock = requestClock;
        _nowMs = nowMs ?? (() => Environment.TickCount64);
        Map = map ?? new ZeldathonMap();
    }

    public ZeldathonMap Map { get; set; }

    /// <summary>Se pide al juego que vuelva a mandar todo su estado (HiveShock arrancó después que el juego).</summary>
    public event Action? SnapshotWanted;

    public bool GameActive
    {
        get
        {
            lock (_gate)
            {
                return _gameActive;
            }
        }
    }

    public IReadOnlyCollection<string> CompletedObjectives
    {
        get
        {
            lock (_gate)
            {
                return OrderedObjectives();
            }
        }
    }

    public double Percentage
    {
        get
        {
            lock (_gate)
            {
                return ComputePercentage();
            }
        }
    }

    /// <summary>Un evento del juego. Los que no son de telemetría (muertes, etc.) se ignoran.</summary>
    public void OnGameEvent(string name, JsonElement? data)
    {
        lock (_gate)
        {
            if (!Apply(name, data))
            {
                return;
            }
        }

        Reconcile();
    }

    /// <summary>Nueva conexión con el servidor: se reenvía todo (los repetidos los ignora el servidor).</summary>
    public void OnConnected()
    {
        lock (_gate)
        {
            ForgetSent();
            _sentBosses.UnionWith(_bosses);
            _startedSent = false;
            _lastStartMs = long.MinValue / 2;
        }

        SnapshotWanted?.Invoke();
        Reconcile();
    }

    /// <summary>El servidor rechazó algo: se da por perdido lo enviado y se reenvía la diferencia.</summary>
    public void OnRejected(string messageType, string code)
    {
        lock (_gate)
        {
            if (messageType is "GAME_PROGRESS" or "STATS_UPDATED" or "ITEM_ACQUIRED" or "AREA_CHANGED" or "GAME_FINISHED"
                || code is "out_of_sequence")
            {
                ForgetSent();
            }
        }
    }

    /// <summary>Envía al servidor lo que le falta, si el estado de la sesión lo permite.</summary>
    public void Reconcile()
    {
        lock (_gate)
        {
            if (!_clock.HasValue || _clock.IsDisconnected)
            {
                return;
            }

            var status = _clock.Status;
            if (!_gameActive)
            {
                if (_startedSent && status is ZeldathonRacerStatus.Live or ZeldathonRacerStatus.Paused)
                {
                    _send(ZeldathonProtocol.Message("SESSION_ENDED", $"end:{_runId}"));
                }

                _startedSent = false;
                return;
            }

            switch (status)
            {
                case ZeldathonRacerStatus.Live or ZeldathonRacerStatus.Paused:
                    _startedSent = true;
                    Flush();
                    break;
                case ZeldathonRacerStatus.Online:
                    TryStart();
                    break;
            }
        }
    }

    // ---- interno (siempre bajo _gate) ----

    private bool Apply(string name, JsonElement? data)
    {
        switch (name.ToLowerInvariant())
        {
            case "game_session":
                var state = Str(data, "state");
                if (state == "loaded")
                {
                    NewRun();
                    _gameActive = true;
                    return true;
                }

                if (state == "exited")
                {
                    _gameActive = false;
                    return true;
                }

                return false;
            case "scene":
                if (Int(data, "scene") is { } scene && Map.Areas.TryGetValue(scene, out var area))
                {
                    _area = area;
                    return true;
                }

                return false;
            case "item":
                return Int(data, "item") is { } item && AddItem(item);
            case "inventory":
                var any = false;
                if (data is { } inv && inv.TryGetProperty("items", out var list) && list.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in list.EnumerateArray())
                    {
                        any |= el.TryGetInt32(out var id) && AddItem(id);
                    }
                }

                return any;
            case "boss_defeated":
                return Int(data, "actor") is { } actor && AddBoss(actor);
            case "stats":
                return ApplyStats(data);
            case "quest":
                return Long(data, "items") is { } mask && ApplyQuest(mask);
            default:
                return false;
        }
    }

    private bool AddItem(int gameItem)
    {
        var changed = false;
        if (Map.Items.TryGetValue(gameItem, out var id))
        {
            changed |= _items.Add(id);
        }

        if (Map.ItemObjectives.TryGetValue(gameItem, out var objective))
        {
            changed |= _objectives.Add(objective);
        }

        return changed;
    }

    private bool AddBoss(int actor)
    {
        if (!Map.Bosses.TryGetValue(actor, out var boss))
        {
            return false;
        }

        var changed = _bosses.Add(boss);
        if (Map.BossObjectives.TryGetValue(boss, out var objective))
        {
            changed |= _objectives.Add(objective);
        }

        if (Map.FinishBoss.Length > 0 && string.Equals(boss, Map.FinishBoss, StringComparison.OrdinalIgnoreCase) && !_finishReady)
        {
            _finishReady = true;
            changed = true;
        }

        return changed;
    }

    private bool ApplyQuest(long mask)
    {
        var changed = false;
        foreach (var (bit, objective) in Map.QuestObjectives)
        {
            if (bit is >= 0 and < 32 && (mask & (1L << bit)) != 0)
            {
                changed |= _objectives.Add(objective);
            }
        }

        return changed;
    }

    private bool ApplyStats(JsonElement? data)
    {
        if (data is not { } el)
        {
            return false;
        }

        var before = (_hearts, _maxHearts, _rupees, _skulltulas);
        _hearts = Num(el, "hearts") ?? _hearts;
        _maxHearts = Num(el, "maxHearts") ?? _maxHearts;
        _rupees = Num(el, "rupees") is { } r ? (long)r : _rupees;
        _skulltulas = Num(el, "skulltulas") is { } s ? (long)s : _skulltulas;
        return before != (_hearts, _maxHearts, _rupees, _skulltulas);
    }

    private void NewRun()
    {
        _runId = Guid.NewGuid().ToString("N")[..8];
        _area = null;
        _items.Clear();
        _objectives.Clear();
        _bosses.Clear();
        _hearts = _maxHearts = null;
        _rupees = _skulltulas = null;
        _finishReady = false;
        ForgetSent();
        _sentBosses.Clear();
        _startedSent = false;
    }

    private void ForgetSent()
    {
        _sentArea = null;
        _sentItems.Clear();
        _sentProgress = "";
        _sentStats = "";
        _sentFinished = false;
    }

    private void TryStart()
    {
        var now = _nowMs();
        if (now - _lastStartMs < StartRetry.TotalMilliseconds)
        {
            return;
        }

        _lastStartMs = now;
        _send(ZeldathonProtocol.Message("SESSION_STARTED", $"start:{_runId}:{++_startAttempts}"));
        // El servidor solo contesta ACK: se pide el reloj ya para saber cuándo quedó en vivo.
        _requestClock?.Invoke();
    }

    private void Flush()
    {
        if (_area != null && _area != _sentArea)
        {
            _send(ZeldathonProtocol.Message("AREA_CHANGED", fields: new JsonObject { ["area"] = _area }));
            _sentArea = _area;
        }

        foreach (var item in _items.Where(i => !_sentItems.Contains(i)).ToList())
        {
            _send(ZeldathonProtocol.Message("ITEM_ACQUIRED", $"item:{_runId}:{item}", new JsonObject { ["item"] = item }));
            _sentItems.Add(item);
        }

        foreach (var boss in _bosses.Where(b => !_sentBosses.Contains(b)).ToList())
        {
            _send(ZeldathonProtocol.Message("BOSS_DEFEATED", $"boss:{_runId}:{boss}", new JsonObject { ["boss"] = boss }));
            _sentBosses.Add(boss);
        }

        var ordered = OrderedObjectives();
        var current = ZeldathonCatalog.Objectives.FirstOrDefault(o => !_objectives.Contains(o));
        var progressKey = $"{ComputePercentage():0.##}|{string.Join(',', ordered)}|{current}|{_area}";
        if (progressKey != _sentProgress)
        {
            var progress = new JsonObject
            {
                ["percentage"] = ComputePercentage(),
                ["completedObjectives"] = new JsonArray(ordered.Select(o => (JsonNode?)JsonValue.Create(o)).ToArray()),
            };
            if (current != null)
            {
                progress["currentObjective"] = current;
            }

            if (_area != null)
            {
                progress["currentArea"] = _area;
            }

            _send(ZeldathonProtocol.Message("GAME_PROGRESS", fields: new JsonObject { ["progress"] = progress }));
            _sentProgress = progressKey;
        }

        var stats = new JsonObject();
        if (_hearts is { } h) stats["hearts"] = h;
        if (_maxHearts is { } m) stats["maxHearts"] = m;
        if (_rupees is { } r) stats["rupees"] = r;
        if (_skulltulas is { } s) stats["skulltulas"] = s;
        // Total absoluto: corrige cualquier desvío del contador de jefes del servidor.
        stats["bossesDefeated"] = _bosses.Count;
        var statsKey = stats.ToJsonString();
        if (statsKey != _sentStats)
        {
            _send(ZeldathonProtocol.Message("STATS_UPDATED", fields: new JsonObject { ["stats"] = stats }));
            _sentStats = statsKey;
        }

        if (_finishReady && !_sentFinished && ZeldathonCatalog.Objectives.All(_objectives.Contains))
        {
            _send(ZeldathonProtocol.Message("GAME_FINISHED", $"finish:{_runId}"));
            _sentFinished = true;
        }
    }

    private List<string> OrderedObjectives() =>
        ZeldathonCatalog.Objectives.Where(_objectives.Contains).ToList();

    private double ComputePercentage() =>
        Math.Round(_objectives.Count(ZeldathonCatalog.IsObjective) * 100.0 / ZeldathonCatalog.Objectives.Count, 1);

    private static string Str(JsonElement? data, string name) =>
        data is { } el && el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? ""
            : "";

    private static int? Int(JsonElement? data, string name) =>
        data is { } el && el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? n : null;

    private static long? Long(JsonElement? data, string name) =>
        data is { } el && el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.TryGetInt64(out var n) ? n : null;

    private static double? Num(JsonElement el, string name) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.TryGetDouble(out var d) && double.IsFinite(d) ? d : null;
}
