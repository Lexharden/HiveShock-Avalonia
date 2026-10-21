using System.Text.Json;
using HiveShock.Configuration;
using HiveShock.Logging;

namespace HiveShock.Live;

/// <summary>Un efecto del juego aceptado y todavía no entregado. Se guarda para no perderlo si HiveShock se cierra.</summary>
public sealed class PendingEffect
{
    public string Id { get; set; } = "";
    public long AtMs { get; set; }
    public string EffectId { get; set; } = "";
    public string? User { get; set; }
    public string Reason { get; set; } = "";
    public bool FromChat { get; set; }
    public Dictionary<string, double>? Params { get; set; }
}

/// <summary>
/// Registro de lo que ya se vio y de lo que está pendiente de entregar. Da «exactamente una vez»:
/// si el canal reenvía un mensaje (historial al reconectar, reintento), se ignora el repetido; y lo aceptado que no
/// llegó al juego sobrevive a un cierre. Se guarda en disco como líneas JSON (la última se tolera dañada).
/// </summary>
public sealed class GiftLedger : IDisposable
{
    public const string FileName = ".hiveshock-ledger.jsonl";

    private static readonly TimeSpan FlushEvery = TimeSpan.FromMilliseconds(500);

    private readonly object _gate = new();
    private readonly string? _path;
    private readonly TimeSpan _window;
    private readonly int _maxKeys;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Dictionary<string, long> _seen = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PendingEffect> _pending = new(StringComparer.Ordinal);
    private readonly List<string> _unsaved = [];
    private readonly Timer? _timer;
    private long _duplicates;
    private long _tooOld;

    public GiftLedger(string? path = null, TimeSpan? window = null, int maxKeys = 5000, Func<DateTimeOffset>? clock = null)
    {
        _path = path;
        _window = window ?? TimeSpan.FromHours(2);
        _maxKeys = Math.Max(10, maxKeys);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        if (_path != null)
        {
            Load();
            _timer = new Timer(_ => Flush(), null, FlushEvery, FlushEvery);
        }
    }

    public static GiftLedger OpenDefault() =>
        new(Path.Combine(UserDataStore.UserDirectory, FileName));

    /// <summary>Mensajes repetidos que se descartaron desde que arrancó.</summary>
    public long Duplicates => Interlocked.Read(ref _duplicates);

    /// <summary>Mensajes demasiado viejos que se ignoraron desde que arrancó.</summary>
    public long TooOld => Interlocked.Read(ref _tooOld);

    public void NoteTooOld() => Interlocked.Increment(ref _tooOld);

    /// <summary>True la primera vez que se ve esta clave (y la registra); false si ya se había visto dentro de la ventana.</summary>
    public bool TryClaim(string key)
    {
        var now = Now();
        lock (_gate)
        {
            if (_seen.TryGetValue(key, out var at) && now - at < _window.TotalMilliseconds)
            {
                Interlocked.Increment(ref _duplicates);
                return false;
            }

            _seen[key] = now;
            if (_seen.Count > _maxKeys)
            {
                TrimLocked(now);
            }

            Append(new Dictionary<string, object?> { ["op"] = "seen", ["k"] = key, ["t"] = now });
            return true;
        }
    }

    public bool Contains(string key)
    {
        lock (_gate)
        {
            return _seen.TryGetValue(key, out var at) && Now() - at < _window.TotalMilliseconds;
        }
    }

    /// <summary>Anota un efecto aceptado que aún no llegó al juego. Se escribe a disco de inmediato.</summary>
    public string AddPending(string effectId, string? user, string reason, bool fromChat, IReadOnlyDictionary<string, double>? parameters)
    {
        var item = new PendingEffect
        {
            Id = Guid.NewGuid().ToString("N"),
            AtMs = Now(),
            EffectId = effectId,
            User = user,
            Reason = reason,
            FromChat = fromChat,
            Params = parameters is { Count: > 0 } ? new Dictionary<string, double>(parameters) : null,
        };
        lock (_gate)
        {
            _pending[item.Id] = item;
            Append(new Dictionary<string, object?> { ["op"] = "fx", ["fx"] = item });
            FlushLocked();
        }

        return item.Id;
    }

    public void MarkDelivered(string? id) => Finish(id, "delivered");

    public void MarkDiscarded(string? id) => Finish(id, "discarded");

    /// <summary>Descarta todo lo pendiente (pausa de emergencia o desconexión a propósito).</summary>
    public int DiscardAllPending()
    {
        string[] ids;
        lock (_gate)
        {
            ids = [.. _pending.Keys];
        }

        foreach (var id in ids)
        {
            Finish(id, "discarded");
        }

        return ids.Length;
    }

    public int PendingCount
    {
        get
        {
            lock (_gate)
            {
                return _pending.Count;
            }
        }
    }

    /// <summary>
    /// Pendientes que todavía valen (no más viejos que <paramref name="maxAge"/>), del más antiguo al más nuevo.
    /// Los que ya caducaron se descartan.
    /// </summary>
    public List<PendingEffect> TakeReplayable(TimeSpan maxAge)
    {
        var now = Now();
        List<PendingEffect> live;
        List<string> expired;
        lock (_gate)
        {
            live = _pending.Values.Where(p => now - p.AtMs <= maxAge.TotalMilliseconds).OrderBy(p => p.AtMs).ToList();
            expired = _pending.Values.Where(p => now - p.AtMs > maxAge.TotalMilliseconds).Select(p => p.Id).ToList();
        }

        foreach (var id in expired)
        {
            Finish(id, "discarded");
        }

        return live;
    }

    public void Flush()
    {
        lock (_gate)
        {
            FlushLocked();
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        Flush();
    }

    private void Finish(string? id, string state)
    {
        if (id == null)
        {
            return;
        }

        lock (_gate)
        {
            if (_pending.Remove(id))
            {
                Append(new Dictionary<string, object?> { ["op"] = "done", ["id"] = id, ["s"] = state });
            }
        }
    }

    private long Now() => _clock().ToUnixTimeMilliseconds();

    private void TrimLocked(long now)
    {
        var cutoff = now - (long)_window.TotalMilliseconds;
        foreach (var key in _seen.Where(kv => kv.Value < cutoff).Select(kv => kv.Key).ToList())
        {
            _seen.Remove(key);
        }

        if (_seen.Count <= _maxKeys)
        {
            return;
        }

        foreach (var key in _seen.OrderBy(kv => kv.Value).Take(_seen.Count - _maxKeys / 2).Select(kv => kv.Key).ToList())
        {
            _seen.Remove(key);
        }
    }

    private void Append(Dictionary<string, object?> line)
    {
        if (_path != null)
        {
            _unsaved.Add(JsonSerializer.Serialize(line));
        }
    }

    private void FlushLocked()
    {
        if (_path == null || _unsaved.Count == 0)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.AppendAllLines(_path, _unsaved);
            _unsaved.Clear();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Se reintenta en el siguiente ciclo; el registro en memoria sigue valiendo.
            if (_unsaved.Count > 10_000)
            {
                _unsaved.Clear();
            }

            BridgeLog.Warn($"Registro de regalos: no se pudo guardar ({ex.Message}).");
        }
    }

    private void Load()
    {
        if (_path == null || !File.Exists(_path))
        {
            return;
        }

        var now = Now();
        var cutoff = now - (long)_window.TotalMilliseconds;
        try
        {
            foreach (var raw in File.ReadLines(_path))
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                try
                {
                    using var doc = JsonDocument.Parse(raw);
                    var root = doc.RootElement;
                    switch (root.GetProperty("op").GetString())
                    {
                        case "seen":
                            var t = root.GetProperty("t").GetInt64();
                            if (t >= cutoff)
                            {
                                _seen[root.GetProperty("k").GetString()!] = t;
                            }

                            break;
                        case "fx":
                            var fx = root.GetProperty("fx").Deserialize<PendingEffect>(JsonDefaults.Options);
                            if (fx is { Id.Length: > 0 })
                            {
                                _pending[fx.Id] = fx;
                            }

                            break;
                        case "done":
                            _pending.Remove(root.GetProperty("id").GetString() ?? "");
                            break;
                    }
                }
                catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
                {
                    // Línea dañada (típico de un cierre brusco): se salta.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            BridgeLog.Warn($"Registro de regalos: no se pudo leer ({ex.Message}).");
            return;
        }

        TrimLocked(now);
        Compact();
    }

    /// <summary>Reescribe el archivo solo con lo vigente para que no crezca sin límite.</summary>
    private void Compact()
    {
        if (_path == null)
        {
            return;
        }

        try
        {
            var lines = _seen.Select(kv => JsonSerializer.Serialize(
                    new Dictionary<string, object?> { ["op"] = "seen", ["k"] = kv.Key, ["t"] = kv.Value }))
                .Concat(_pending.Values.Select(p => JsonSerializer.Serialize(
                    new Dictionary<string, object?> { ["op"] = "fx", ["fx"] = p })));
            AtomicFile.WriteAllText(_path, string.Join('\n', lines) + (_seen.Count + _pending.Count > 0 ? "\n" : ""));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            BridgeLog.Warn($"Registro de regalos: no se pudo compactar ({ex.Message}).");
        }
    }
}
