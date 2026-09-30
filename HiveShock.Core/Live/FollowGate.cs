using HiveShock.Configuration;
using HiveShock.Logging;

namespace HiveShock.Live;

public enum FollowAdmitKind
{
    Allowed,
    /// <summary>Ese usuario ya disparó el efecto de seguir antes (seguir/dejar de seguir en bucle).</summary>
    Duplicate,
}

/// <summary>
/// Anti-spam de "seguir": cada usuario dispara el efecto una sola vez, para siempre. Quien sigue y deja
/// de seguir una y otra vez no vuelve a activarlo. La identidad es el id estable del usuario (no el
/// nombre visible) y la clave incluye puerto y canal. Se guarda en disco con <see cref="UserDataStore"/>
/// (con espera, no en cada follow) para sobrevivir a reinicios y a directos futuros.
/// </summary>
public sealed class FollowGate : IDisposable
{
    public const string FileName = ".hiveshock-followers.json";
    public const int MaxTracked = 200_000;

    private static readonly TimeSpan FlushEvery = TimeSpan.FromSeconds(5);

    private readonly object _gate = new();
    private readonly Dictionary<string, long> _seen;
    private readonly Func<Dictionary<string, long>?> _load;
    private readonly Action<Dictionary<string, long>> _save;
    private readonly Func<DateTime> _now;
    private readonly Timer? _flushTimer;
    private bool _dirty;

    /// <summary>Con almacenamiento en disco (uso normal).</summary>
    public FollowGate()
        : this(
            () => UserDataStore.Load<Dictionary<string, long>>(FileName),
            data => UserDataStore.Save(FileName, data),
            () => DateTime.UtcNow,
            autoFlush: true)
    {
    }

    /// <param name="load">Devuelve lo guardado o null. Puede lanzar: se ignora y se arranca vacío.</param>
    /// <param name="save">Escribe el conjunto completo.</param>
    public FollowGate(
        Func<Dictionary<string, long>?> load,
        Action<Dictionary<string, long>> save,
        Func<DateTime> now,
        bool autoFlush = false)
    {
        _load = load;
        _save = save;
        _now = now;
        Dictionary<string, long>? stored = null;
        try
        {
            stored = _load();
        }
        catch (Exception ex)
        {
            BridgeLog.Warn($"Historial de seguidores ilegible, se empieza vacío: {ex.Message}");
        }

        _seen = stored != null
            ? new Dictionary<string, long>(stored, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        if (autoFlush)
        {
            _flushTimer = new Timer(_ => Flush(), null, FlushEvery, FlushEvery);
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _seen.Count;
            }
        }
    }

    /// <summary>Clave estable: puerto + canal + id del usuario. Sin id usable, "anon".</summary>
    public static string Key(string portId, string? channel, string? userId)
    {
        var id = string.IsNullOrWhiteSpace(userId) ? "anon" : userId.Trim();
        var ch = string.IsNullOrWhiteSpace(channel) ? "-" : channel.Trim().TrimStart('@').ToLowerInvariant();
        return $"{portId}:{ch}:{id}";
    }

    /// <summary>Atómico: si es la primera vez lo registra y devuelve Allowed; si no, Duplicate.</summary>
    public FollowAdmitKind TryAdmit(string key)
    {
        lock (_gate)
        {
            if (_seen.ContainsKey(key))
            {
                return FollowAdmitKind.Duplicate;
            }

            _seen[key] = new DateTimeOffset(_now()).ToUnixTimeSeconds();
            _dirty = true;
            if (_seen.Count > MaxTracked)
            {
                Trim();
            }

            return FollowAdmitKind.Allowed;
        }
    }

    /// <summary>Escribe a disco si hubo cambios. Nunca lanza.</summary>
    public void Flush()
    {
        Dictionary<string, long> snapshot;
        lock (_gate)
        {
            if (!_dirty)
            {
                return;
            }

            snapshot = new Dictionary<string, long>(_seen, StringComparer.OrdinalIgnoreCase);
            _dirty = false;
        }

        try
        {
            _save(snapshot);
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                _dirty = true;
            }

            BridgeLog.Warn($"No se pudo guardar el historial de seguidores: {ex.Message}");
        }
    }

    /// <summary>Olvida a todos (memoria y disco). Para pruebas o empezar de cero.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _seen.Clear();
            _dirty = true;
        }

        Flush();
    }

    /// <summary>Vacía el archivo sin necesitar una instancia (cuando el puente está detenido).</summary>
    public static void ClearPersisted() => UserDataStore.Save(FileName, new Dictionary<string, long>());

    public void Dispose()
    {
        _flushTimer?.Dispose();
        Flush();
    }

    private void Trim()
    {
        // Descarta el 10 % más antiguo para no recortar en cada follow nuevo.
        var drop = _seen.OrderBy(kv => kv.Value).Take(Math.Max(1, MaxTracked / 10)).Select(kv => kv.Key).ToList();
        foreach (var key in drop)
        {
            _seen.Remove(key);
        }
    }
}
