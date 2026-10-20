using System.Text.Json.Serialization;
using HiveShock.Configuration;
using HiveShock.Logging;

namespace HiveShock.Live;

public enum ViewerVerdict
{
    Allowed,

    /// <summary>El streamer pausó los efectos de espectadores.</summary>
    Paused,

    /// <summary>El streamer bloqueó a esta persona.</summary>
    Blocked,
}

/// <summary>Una persona bloqueada. Se guarda en disco.</summary>
public sealed class BlockedViewer
{
    public string Id { get; set; } = "";
    public string PortId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Handle { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime AddedUtc { get; set; }

    [JsonIgnore]
    public ViewerIdentity Identity => new(PortId, UserId, Handle, Name);

    public BlockedViewer Clone() => (BlockedViewer)MemberwiseClone();
}

public sealed class ViewerBlocklistFile
{
    public List<BlockedViewer> Viewers { get; set; } = [];
}

/// <summary>Un evento de un espectador que activó (o intentó activar) un efecto, para la lista de moderación.</summary>
public sealed record ViewerActivity(DateTime AtUtc, ViewerIdentity Viewer, string What, ViewerVerdict Verdict);

/// <summary>
/// Freno manual del streamer sobre los efectos que mandan los espectadores: pausa de emergencia y lista de
/// bloqueo. Vive todo el programa (no una sesión del puente), así sobrevive a desconectar y volver a conectar.
/// <list type="bullet">
/// <item>La pausa no se guarda: al abrir HiveShock siempre empieza reanudada, para no dejar un directo sin
/// efectos por olvido.</item>
/// <item>La lista de bloqueo se guarda con <see cref="UserDataStore"/> (copia redundante y respaldo).</item>
/// <item>Una persona se reconoce por su id; el @usuario solo cuenta si falta el id (un @usuario se puede
/// cambiar y otra persona puede quedarse con el viejo). Al ver el id de alguien bloqueado solo por @usuario,
/// se aprende; y si cambia de @usuario, se actualiza.</item>
/// </list>
/// </summary>
public sealed class ViewerGuard
{
    public const string FileName = ".hiveshock-blocklist.json";
    public const int MaxRecent = 60;
    public const int MaxBlocked = 5000;

    private readonly object _gate = new();
    private readonly Action<ViewerBlocklistFile> _save;
    private readonly Func<DateTime> _utcNow;
    private readonly List<BlockedViewer> _blocked = [];
    private readonly LinkedList<ViewerActivity> _recent = new();
    private volatile BlockIndex _index = BlockIndex.Empty;
    private volatile bool _paused;

    public ViewerGuard(
        Func<ViewerBlocklistFile?>? load = null,
        Action<ViewerBlocklistFile>? save = null,
        Func<DateTime>? utcNow = null)
    {
        _save = save ?? (file => UserDataStore.Save(FileName, file));
        _utcNow = utcNow ?? (() => DateTime.UtcNow);

        ViewerBlocklistFile? stored = null;
        try
        {
            stored = (load ?? (() => UserDataStore.Load<ViewerBlocklistFile>(FileName)))();
        }
        catch (Exception ex)
        {
            BridgeLog.Warn($"Lista de bloqueados ilegible, se empieza vacía: {ex.Message}");
        }

        foreach (var entry in stored?.Viewers ?? [])
        {
            var clean = Sanitize(entry);
            if (clean != null && _blocked.Count < MaxBlocked)
            {
                _blocked.Add(clean);
            }
        }

        _index = BlockIndex.Build(_blocked);
    }

    /// <summary>Algo cambió: pausa, lista de bloqueo o actividad reciente. Puede llegar desde cualquier hilo.</summary>
    public event Action? Changed;

    /// <summary>La pausa se activó o se quitó.</summary>
    public event Action<bool>? PausedChanged;

    public bool IsPaused => _paused;

    public void SetPaused(bool paused)
    {
        if (_paused == paused)
        {
            return;
        }

        _paused = paused;
        BridgeLog.Info(paused ? "Efectos de espectadores pausados." : "Efectos de espectadores reanudados.");
        PausedChanged?.Invoke(paused);
        Changed?.Invoke();
    }

    /// <summary>Bloqueado manda sobre pausado. No anota nada en la actividad reciente.</summary>
    public ViewerVerdict Evaluate(ViewerIdentity viewer) =>
        FindBlocked(viewer) != null ? ViewerVerdict.Blocked : _paused ? ViewerVerdict.Paused : ViewerVerdict.Allowed;

    public bool IsBlocked(ViewerIdentity viewer) => FindBlocked(viewer) != null;

    /// <summary>Evalúa y anota el evento en la actividad reciente (para poder bloquear a quien lo mandó).</summary>
    public ViewerVerdict Check(ViewerIdentity viewer, string what)
    {
        var verdict = Evaluate(viewer);
        Record(viewer, what, verdict);
        return verdict;
    }

    /// <summary>Los bloqueados no se anotan: un spammer ya bloqueado llenaría la lista y taparía a los demás.</summary>
    public void Record(ViewerIdentity viewer, string what, ViewerVerdict verdict)
    {
        if (verdict == ViewerVerdict.Blocked)
        {
            return;
        }

        lock (_gate)
        {
            _recent.AddFirst(new ViewerActivity(_utcNow(), viewer, what, verdict));
            while (_recent.Count > MaxRecent)
            {
                _recent.RemoveLast();
            }
        }

        Changed?.Invoke();
    }

    /// <summary>Lo último que activó un efecto, lo más reciente primero.</summary>
    public IReadOnlyList<ViewerActivity> Recent
    {
        get
        {
            lock (_gate)
            {
                return _recent.ToList();
            }
        }
    }

    /// <summary>Los bloqueados, el más reciente primero. Son copias: cambiarlas no afecta a la lista.</summary>
    public IReadOnlyList<BlockedViewer> Blocked
    {
        get
        {
            lock (_gate)
            {
                return _blocked.OrderByDescending(b => b.AddedUtc).Select(b => b.Clone()).ToList();
            }
        }
    }

    /// <summary>Bloquea a una persona. False si ya estaba, si no hay forma de reconocerla o si la lista está llena.</summary>
    public bool Block(ViewerIdentity viewer)
    {
        if (!viewer.IsIdentified || viewer.PortId.Length == 0)
        {
            return false;
        }

        lock (_gate)
        {
            if (Lookup(viewer) != null || _blocked.Count >= MaxBlocked)
            {
                return false;
            }

            _blocked.Add(new BlockedViewer
            {
                Id = Guid.NewGuid().ToString("N"),
                PortId = viewer.PortId,
                UserId = viewer.UserId,
                Handle = viewer.Handle,
                Name = viewer.Name,
                AddedUtc = _utcNow(),
            });
            Commit();
        }

        BridgeLog.Info($"Bloqueado {viewer.Detail} ({viewer.PortId}): sus eventos ya no activan efectos.");
        Changed?.Invoke();
        return true;
    }

    /// <summary>Bloquea por @usuario (cuando el streamer lo escribe a mano). El id se aprende al verlo.</summary>
    public bool BlockHandle(string portId, string handle) =>
        Block(new ViewerIdentity(portId, null, handle, handle));

    public bool Unblock(string id)
    {
        BlockedViewer? removed;
        lock (_gate)
        {
            removed = _blocked.FirstOrDefault(b => b.Id == id);
            if (removed == null)
            {
                return false;
            }

            _blocked.Remove(removed);
            Commit();
        }

        BridgeLog.Info($"Desbloqueado {removed.Identity.Detail} ({removed.PortId}).");
        Changed?.Invoke();
        return true;
    }

    private BlockedViewer? FindBlocked(ViewerIdentity viewer)
    {
        var entry = Lookup(viewer);
        if (entry != null)
        {
            Learn(entry, viewer);
        }

        return entry;
    }

    private BlockedViewer? Lookup(ViewerIdentity viewer)
    {
        var index = _index;
        if (index.IsEmpty)
        {
            return null;
        }

        BlockedViewer? entry = null;
        if (viewer.HasId && index.ById.TryGetValue(Key(viewer.PortId, viewer.UserId), out var byId))
        {
            entry = byId;
        }
        else if (viewer.Handle.Length > 0 &&
                 index.ByHandle.TryGetValue(Key(viewer.PortId, viewer.Handle), out var byHandle) &&
                 (!viewer.HasId || byHandle.UserId.Length == 0))
        {
            // Si los dos tienen id y no coinciden, es otra persona que se quedó con el @usuario.
            entry = byHandle;
        }

        return entry;
    }

    /// <summary>Completa el id de quien se bloqueó por @usuario y sigue a quien cambia de @usuario.</summary>
    private void Learn(BlockedViewer entry, ViewerIdentity viewer)
    {
        var needsId = viewer.HasId && entry.UserId.Length == 0;
        var renamed = viewer.HasId && viewer.Handle.Length > 0 && entry.UserId == viewer.UserId &&
                      !string.Equals(entry.Handle, viewer.Handle, StringComparison.OrdinalIgnoreCase);
        if (!needsId && !renamed)
        {
            return;
        }

        lock (_gate)
        {
            if (!_blocked.Contains(entry))
            {
                return;
            }

            if (needsId && entry.UserId.Length == 0)
            {
                entry.UserId = viewer.UserId;
            }

            if (viewer.HasId && entry.UserId == viewer.UserId && viewer.Handle.Length > 0)
            {
                entry.Handle = viewer.Handle;
            }

            if (viewer.Name.Length > 0)
            {
                entry.Name = viewer.Name;
            }

            Commit();
        }

        Changed?.Invoke();
    }

    /// <summary>Reconstruye el índice y guarda. Siempre bajo <c>_gate</c>.</summary>
    private void Commit()
    {
        _index = BlockIndex.Build(_blocked);
        try
        {
            _save(new ViewerBlocklistFile { Viewers = _blocked.Select(b => b.Clone()).ToList() });
        }
        catch (Exception ex)
        {
            BridgeLog.Warn($"No se pudo guardar la lista de bloqueados: {ex.Message}");
        }
    }

    private static BlockedViewer? Sanitize(BlockedViewer? entry)
    {
        if (entry == null)
        {
            return null;
        }

        var identity = new ViewerIdentity(entry.PortId, entry.UserId, entry.Handle, entry.Name);
        if (!identity.IsIdentified || identity.PortId.Length == 0)
        {
            return null;
        }

        return new BlockedViewer
        {
            Id = string.IsNullOrWhiteSpace(entry.Id) ? Guid.NewGuid().ToString("N") : entry.Id,
            PortId = identity.PortId,
            UserId = identity.UserId,
            Handle = identity.Handle,
            Name = identity.Name,
            AddedUtc = entry.AddedUtc,
        };
    }

    private static string Key(string portId, string value) => $"{portId}:{value}";

    /// <summary>Búsqueda rápida sin bloquear: se reemplaza entera en cada cambio de la lista.</summary>
    private sealed class BlockIndex
    {
        public static readonly BlockIndex Empty = new();

        public Dictionary<string, BlockedViewer> ById { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, BlockedViewer> ByHandle { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool IsEmpty => ById.Count == 0 && ByHandle.Count == 0;

        public static BlockIndex Build(IEnumerable<BlockedViewer> entries)
        {
            var index = new BlockIndex();
            foreach (var entry in entries)
            {
                if (entry.UserId.Length > 0)
                {
                    index.ById[Key(entry.PortId, entry.UserId)] = entry;
                }

                if (entry.Handle.Length > 0)
                {
                    index.ByHandle[Key(entry.PortId, entry.Handle)] = entry;
                }
            }

            return index;
        }
    }
}
