using HiveShock.Configuration;

namespace HiveShock.Live;

public sealed class GoalSnapshot
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public int Count { get; init; }
    public int Need { get; init; } = 2;
    public string Effect { get; init; } = "";
    public bool Closed { get; init; }
    public bool AlsoInstant { get; init; } = true;
    public bool Repeat { get; init; } = true;
    public IReadOnlyList<string> Keys { get; init; } = [];

    public string ProgressText => Closed && !Repeat
        ? $"{Need}/{Need}"
        : $"{Count}/{Need}";
}

public sealed class GoalTick
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public string Effect { get; init; } = "";
    public int Count { get; init; }
    public int Need { get; init; }
    public int Added { get; init; }
    public bool Fired { get; init; }
    public bool AlsoInstant { get; init; } = true;
    public bool Closed { get; init; }
}

/// <summary>
/// Acumulador atómico de regalos entre varios viewers. Un disparo por cruce;
/// el resto del mismo lote se guarda (count % need).
/// </summary>
public sealed class GiftGoalBank
{
    private readonly object _gate = new();
    private IReadOnlyList<GiftGoalConfig> _defs = [];
    private readonly Dictionary<string, GoalState> _state = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler? Changed;

    public IReadOnlyList<GoalSnapshot> Snapshots()
    {
        lock (_gate)
        {
            return _defs.Select(ToSnapshot).ToList();
        }
    }

    public void ApplyDefinitions(IReadOnlyList<GiftGoalConfig> defs)
    {
        lock (_gate)
        {
            _defs = defs ?? [];
            var keep = new HashSet<string>(_defs.Select(d => d.Id), StringComparer.OrdinalIgnoreCase);
            foreach (var id in _state.Keys.ToList())
            {
                if (!keep.Contains(id))
                {
                    _state.Remove(id);
                }
            }

            foreach (var def in _defs)
            {
                if (!_state.ContainsKey(def.Id))
                {
                    _state[def.Id] = new GoalState();
                }
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ResetAll()
    {
        lock (_gate)
        {
            foreach (var state in _state.Values)
            {
                state.Count = 0;
                state.Closed = false;
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Reset(string id)
    {
        lock (_gate)
        {
            if (_state.TryGetValue(id, out var state))
            {
                state.Count = 0;
                state.Closed = false;
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public GoalTick? Contribute(string? name, string? id, int units)
    {
        if (units <= 0)
        {
            return null;
        }

        var nName = GiftKeyNormalizer.Normalize(name);
        var nId = GiftKeyNormalizer.Normalize(id);

        GoalTick? tick = null;
        lock (_gate)
        {
            var def = FindMatch(nName, nId);
            if (def == null)
            {
                return null;
            }

            if (!_state.TryGetValue(def.Id, out var state))
            {
                state = new GoalState();
                _state[def.Id] = state;
            }

            if (state.Closed)
            {
                tick = ToTick(def, state, added: 0, fired: false);
            }
            else
            {
                state.Count += units;
                var fired = false;
                if (state.Count >= def.Need && !string.IsNullOrWhiteSpace(def.Effect))
                {
                    fired = true;
                    state.Count %= def.Need;
                    if (!def.Repeat)
                    {
                        state.Closed = true;
                        state.Count = 0;
                    }
                }

                tick = ToTick(def, state, units, fired);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return tick;
    }

    private GiftGoalConfig? FindMatch(string nName, string nId)
    {
        foreach (var def in _defs)
        {
            foreach (var key in def.Keys)
            {
                if (key.Length == 0)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(nName) &&
                    string.Equals(key, nName, StringComparison.OrdinalIgnoreCase))
                {
                    return def;
                }

                if (!string.IsNullOrEmpty(nId) &&
                    string.Equals(key, nId, StringComparison.OrdinalIgnoreCase))
                {
                    return def;
                }
            }
        }

        return null;
    }

    private GoalSnapshot ToSnapshot(GiftGoalConfig def)
    {
        _state.TryGetValue(def.Id, out var state);
        state ??= new GoalState();
        return new GoalSnapshot
        {
            Id = def.Id,
            Label = def.Label,
            Count = state.Closed ? def.Need : state.Count,
            Need = def.Need,
            Effect = def.Effect,
            Closed = state.Closed,
            AlsoInstant = def.AlsoInstant,
            Repeat = def.Repeat,
            Keys = def.Keys,
        };
    }

    private static GoalTick ToTick(GiftGoalConfig def, GoalState state, int added, bool fired) =>
        new()
        {
            Id = def.Id,
            Label = def.Label,
            Effect = def.Effect,
            Count = state.Closed ? def.Need : state.Count,
            Need = def.Need,
            Added = added,
            Fired = fired,
            AlsoInstant = def.AlsoInstant,
            Closed = state.Closed,
        };

    private sealed class GoalState
    {
        public int Count;
        public bool Closed;
    }
}
