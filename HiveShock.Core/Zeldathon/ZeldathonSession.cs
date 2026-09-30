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
    private string? _age;
    private double? _hearts;
    private double? _maxHearts;
    private long? _rupees;
    private long? _skulltulas;
    private bool _finishReady;

    // Lo que el juego dijo, sin traducir: al cambiar el mapa o el catálogo se vuelve a derivar todo de aquí.
    private readonly HashSet<int> _rawItems = [];
    private readonly HashSet<int> _rawBosses = [];
    private long _rawQuest;
    private readonly Dictionary<string, int> _rawUpgrades = new(StringComparer.OrdinalIgnoreCase);

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
    private readonly HashSet<string> _warnedUnknown = new(StringComparer.Ordinal);

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
        _map = map ?? new ZeldathonMap();
    }

    private ZeldathonMap _map;
    private ZeldathonCatalog _catalog = ZeldathonCatalog.Default;

    /// <summary>Traducción juego → catálogo. Al cambiarla se recalcula todo con lo que el juego ya dijo.</summary>
    public ZeldathonMap Map
    {
        get => _map;
        set
        {
            lock (_gate)
            {
                _map = value;
                Rederive();
            }

            Reconcile();
        }
    }

    /// <summary>
    /// Lo que el servidor acepta (se descarga al conectar; mientras tanto, el catálogo de fábrica). Un ítem que
    /// el juego ya reportó y que antes era desconocido se envía en cuanto el catálogo lo incluye.
    /// </summary>
    public ZeldathonCatalog Catalog
    {
        get => _catalog;
        set
        {
            lock (_gate)
            {
                _catalog = value;
                Rederive();
            }

            Reconcile();
        }
    }

    /// <summary>
    /// Objetivos que exigen las reglas del evento para terminar (<c>/api/event</c>). Sin dato se usan los
    /// marcados como requeridos en el catálogo.
    /// </summary>
    public IReadOnlyCollection<string>? RequiredObjectives { get; set; }

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
            case "upgrades":
                return ApplyUpgrades(data);
            default:
                return false;
        }
    }

    private bool AddItem(int gameItem)
    {
        _rawItems.Add(gameItem);
        return DeriveItem(gameItem);
    }

    private bool DeriveItem(int gameItem)
    {
        var changed = false;
        if (Map.Items.TryGetValue(gameItem, out var id))
        {
            changed |= GrantItem(id);
        }

        if (Map.ItemObjectives.TryGetValue(gameItem, out var objective))
        {
            changed |= GrantObjective(objective);
        }

        return changed;
    }

    /// <summary>Solo se envían ítems que el servidor conoce (si no, contestaría <c>invalid</c>).</summary>
    private bool GrantItem(string id)
    {
        if (Catalog.IsItem(id))
        {
            return _items.Add(id);
        }

        WarnUnknown("ítem", id);
        return false;
    }

    private bool GrantObjective(string id)
    {
        if (Catalog.IsObjective(id))
        {
            return _objectives.Add(id);
        }

        WarnUnknown("objetivo", id);
        return false;
    }

    private void WarnUnknown(string what, string id)
    {
        if (_warnedUnknown.Add($"{what}:{id}"))
        {
            Logging.BridgeLog.Warn($"Zeldathon: el servidor no conoce el {what} «{id}» (zeldathon.json); no se enviará.");
        }
    }

    private bool AddBoss(int actor)
    {
        _rawBosses.Add(actor);
        return DeriveBoss(actor);
    }

    private bool DeriveBoss(int actor)
    {
        if (!Map.Bosses.TryGetValue(actor, out var boss))
        {
            return false;
        }

        var changed = _bosses.Add(boss);
        if (Map.BossObjectives.TryGetValue(boss, out var objective))
        {
            changed |= GrantObjective(objective);
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
        _rawQuest |= mask;
        return DeriveQuest(_rawQuest);
    }

    private bool DeriveQuest(long mask)
    {
        var changed = false;
        foreach (var (bit, objective) in Map.QuestObjectives)
        {
            if (bit is >= 0 and < 32 && (mask & (1L << bit)) != 0)
            {
                changed |= GrantObjective(objective);
            }
        }

        foreach (var (bit, item) in Map.QuestItems)
        {
            if (bit is >= 0 and < 32 && (mask & (1L << bit)) != 0)
            {
                changed |= GrantItem(item);
            }
        }

        return changed;
    }

    /// <summary>Mejoras del juego (bolsa de bombas, monedero, fuerza, escama, magia…) → ítems por nivel.</summary>
    private bool ApplyUpgrades(JsonElement? data)
    {
        if (data is not { ValueKind: JsonValueKind.Object } el)
        {
            return false;
        }

        var changed = false;
        foreach (var (field, _) in Map.Upgrades)
        {
            if (!el.TryGetProperty(field, out var value))
            {
                continue;
            }

            var level = value.ValueKind switch
            {
                JsonValueKind.True => 1,
                JsonValueKind.False => 0,
                _ when value.TryGetInt32(out var n) => n,
                _ => 0,
            };
            _rawUpgrades[field] = Math.Max(level, _rawUpgrades.GetValueOrDefault(field));
            changed |= DeriveUpgrade(field);
        }

        return changed;
    }

    private bool DeriveUpgrade(string field)
    {
        var changed = false;
        if (Map.Upgrades.TryGetValue(field, out var levels) && _rawUpgrades.TryGetValue(field, out var level))
        {
            foreach (var (needed, item) in levels)
            {
                if (level >= needed)
                {
                    changed |= GrantItem(item);
                }
            }
        }

        return changed;
    }

    /// <summary>Recalcula ítems, objetivos y jefes a partir de lo que el juego reportó (cambió el mapa o el catálogo).</summary>
    private void Rederive()
    {
        _items.Clear();
        _objectives.Clear();
        _bosses.Clear();
        _finishReady = false;
        foreach (var item in _rawItems)
        {
            DeriveItem(item);
        }

        if (_rawQuest != 0)
        {
            DeriveQuest(_rawQuest);
        }

        foreach (var field in _rawUpgrades.Keys)
        {
            DeriveUpgrade(field);
        }

        foreach (var actor in _rawBosses)
        {
            DeriveBoss(actor);
        }
    }

    private bool ApplyStats(JsonElement? data)
    {
        if (data is not { } el)
        {
            return false;
        }

        var before = (_hearts, _maxHearts, _rupees, _skulltulas, _age);
        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("age", out var age) && age.ValueKind == JsonValueKind.String)
        {
            _age = age.GetString() is "child" or "adult" ? age.GetString() : _age;
        }

        _hearts = Num(el, "hearts") ?? _hearts;
        _maxHearts = Num(el, "maxHearts") ?? _maxHearts;
        _rupees = Num(el, "rupees") is { } r ? (long)r : _rupees;
        _skulltulas = Num(el, "skulltulas") is { } s ? (long)s : _skulltulas;
        return before != (_hearts, _maxHearts, _rupees, _skulltulas, _age);
    }

    private void NewRun()
    {
        _runId = Guid.NewGuid().ToString("N")[..8];
        _area = null;
        _rawItems.Clear();
        _rawBosses.Clear();
        _rawQuest = 0;
        _rawUpgrades.Clear();
        _items.Clear();
        _objectives.Clear();
        _bosses.Clear();
        _age = null;
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
        var current = Catalog.Objectives.FirstOrDefault(o => !_objectives.Contains(o));
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
        if (_age != null) stats["age"] = _age;
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

        if (_finishReady && !_sentFinished && (RequiredObjectives ?? Catalog.Required).All(_objectives.Contains))
        {
            _send(ZeldathonProtocol.Message("GAME_FINISHED", $"finish:{_runId}"));
            _sentFinished = true;
        }
    }

    private List<string> OrderedObjectives() =>
        Catalog.Objectives.Where(_objectives.Contains).ToList();

    private double ComputePercentage() =>
        Catalog.Objectives.Count == 0
            ? 0
            : Math.Round(_objectives.Count(Catalog.IsObjective) * 100.0 / Catalog.Objectives.Count, 1);

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
