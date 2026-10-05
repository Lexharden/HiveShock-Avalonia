using HiveShock.Configuration;
using HiveShock.Logging;

namespace HiveShock.Zeldathon;

/// <summary>Una donación convertida en tiempo que el servidor aún no confirmó. Se guarda en disco.</summary>
public sealed class PendingDonation
{
    public string Id { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public long DeltaSeconds { get; set; }
    public DonationPlatform Platform { get; set; }
    public long Amount { get; set; }
    public string? Gift { get; set; }
    public int? GiftCount { get; set; }
    public string? Viewer { get; set; }

    public ZeldathonOutbound ToMessage() =>
        ZeldathonProtocol.TimeDonation(Id, DeltaSeconds, Platform, Amount, Gift, GiftCount, Viewer);
}

/// <summary>Lo que se guarda: pendientes de confirmar y el sobrante de cada tarifa.</summary>
public sealed class DonationJournal
{
    public List<PendingDonation> Pending { get; set; } = [];

    /// <summary>Sobrante por plataforma y tarifa ("TikTok|10:1" → 7), para no perder fracciones.</summary>
    public Dictionary<string, long> Carry { get; set; } = [];
}

/// <summary>Qué pasó con una donación (para la lista de la página).</summary>
public sealed record DonationTimeEntry(
    string Id,
    DateTime AtUtc,
    DonationPlatform Platform,
    string What,
    string Viewer,
    long RequestedSeconds,
    long? AppliedSeconds,
    string Status);

/// <summary>
/// El cronómetro manual de la pantalla. <see cref="IsActive"/> dice si es el que se está usando y
/// <see cref="Apply"/> le suma (o resta) los segundos y devuelve los que de verdad cambió (el cronómetro
/// no baja de cero).
/// </summary>
public sealed record LocalTimerHook(Func<bool> IsActive, Func<long, long> Apply);

/// <summary>El servidor aplicó una donación al reloj oficial. <see cref="Seconds"/> es lo aplicado de verdad (con signo).</summary>
public sealed record DonationTimeApplied(long Seconds, long RequestedSeconds, string LimitedBy, DateTime CreatedUtc);

/// <summary>
/// Tiempo por donaciones: convierte regalos de TikTok (diamantes) y bits de Twitch en segundos con la
/// tarifa del streamer y los manda al servidor de Zeldatón (TIME_DONATION), que es quien decide.
/// Robusto ante cortes: cada donación se guarda en disco hasta que el servidor la confirma y se reenvía
/// con el mismo id al reconectar o al volver a abrir HiveShock; el servidor nunca aplica dos veces un id.
/// </summary>
public sealed class DonationTimeReporter
{
    public const string FileName = ".hiveshock-zeldathon-donations.json";

    /// <summary>Una donación sin confirmar durante tanto tiempo ya no representa el directo: se descarta.</summary>
    public static readonly TimeSpan MaxPendingAge = TimeSpan.FromHours(12);

    /// <summary>Tope de pendientes en disco (si el servidor no responde durante horas en un directo enorme).</summary>
    public const int MaxPending = 5000;

    private const int MaxRecent = 30;

    private readonly object _gate = new();
    private readonly Func<DonationTimeSettings> _settings;
    private readonly Func<bool> _active;
    private readonly Action<ZeldathonOutbound> _send;
    private readonly Action<DonationJournal> _save;
    private readonly Func<DateTime> _utcNow;
    private readonly DonationJournal _journal;
    private readonly LinkedList<DonationTimeEntry> _recent = new();
    private readonly HashSet<string> _warned = [];
    private DonationTimePolicy? _policy;

    public DonationTimeReporter(
        Func<DonationTimeSettings> settings,
        Func<bool> active,
        Action<ZeldathonOutbound> send,
        Func<DonationJournal?>? load = null,
        Action<DonationJournal>? save = null,
        Func<DateTime>? utcNow = null)
    {
        _settings = settings;
        _active = active;
        _send = send;
        _save = save ?? (j => UserDataStore.Save(FileName, j));
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _journal = (load ?? (() => UserDataStore.Load<DonationJournal>(FileName)))() ?? new DonationJournal();
        DropExpired();
    }

    /// <summary>Algo cambió (nueva donación, confirmación, rechazo): para refrescar la pantalla.</summary>
    public event Action? Changed;

    /// <summary>
    /// El servidor aplicó una donación y cambió el reloj (no se dispara con rechazos, repetidas ni cambios de 0 s).
    /// Llega ya con el reloj oficial actualizado. Puede venir de cualquier hilo.
    /// </summary>
    public event Action<DonationTimeApplied>? Applied;

    /// <summary>
    /// Cronómetro manual. Si está en uso, cada donación con tarifa se aplica también a él, esté o no conectado el
    /// servidor de la carrera: no hay topes del organizador ni confirmación, solo la tarifa del streamer.
    /// </summary>
    public LocalTimerHook? LocalTimer { get; set; }

    /// <summary>Límites del organizador según /api/event (null = aún no se saben; el servidor decide).</summary>
    public DonationTimePolicy? Policy
    {
        get
        {
            lock (_gate)
            {
                return _policy;
            }
        }
        set
        {
            lock (_gate)
            {
                _policy = value;
            }

            Changed?.Invoke();
        }
    }

    public int PendingCount
    {
        get
        {
            lock (_gate)
            {
                return _journal.Pending.Count;
            }
        }
    }

    /// <summary>Tiempo confirmado por el servidor desde que se abrió HiveShock.</summary>
    public long AddedSeconds { get; private set; }

    public long RemovedSeconds { get; private set; }

    /// <summary>Últimas donaciones, la más reciente primero.</summary>
    public IReadOnlyList<DonationTimeEntry> Recent
    {
        get
        {
            lock (_gate)
            {
                return _recent.ToList();
            }
        }
    }

    /// <summary>Un regalo de TikTok (un combo entero cuenta una vez). True si se envió tiempo.</summary>
    public bool OnTikTokGift(string? viewer, string? gift, int count, long diamonds) =>
        OnDonation(DonationPlatform.TikTok, viewer, diamonds, gift, Math.Max(1, count));

    /// <summary>Bits de Twitch. True si se envió tiempo.</summary>
    public bool OnTwitchBits(string? viewer, int bits) =>
        OnDonation(DonationPlatform.Twitch, viewer, bits, null, null);

    private bool OnDonation(DonationPlatform platform, string? viewer, long amount, string? gift, int? count)
    {
        var settings = _settings();
        if (!settings.Enabled || amount <= 0)
        {
            return false;
        }

        var rule = settings.For(platform);
        if (!rule.Enabled)
        {
            return false;
        }

        var localTimer = LocalTimer is { } hook && hook.IsActive() ? hook : null;
        var server = _active();
        if (!server && localTimer == null)
        {
            return false;
        }

        var what = Describe(platform, amount, gift, count);
        PendingDonation? pending = null;
        long signedSeconds;
        lock (_gate)
        {
            var organizerAllows = true;
            if (server && _policy != null && !_policy.Allows(rule.Direction))
            {
                var why = !_policy.Enabled
                    ? "el organizador desactivó el tiempo por donaciones"
                    : rule.Direction == DonationTimeDirection.Add
                        ? "el organizador no permite que las donaciones sumen tiempo"
                        : "el organizador no permite que las donaciones resten tiempo";
                if (_warned.Add(why))
                {
                    BridgeLog.Warn($"Zeldatón: {why}; las donaciones no cambian tu tiempo.");
                }

                Remember(new DonationTimeEntry("", _utcNow(), platform, what, viewer ?? "", 0, null, $"No enviado: {why}"));
                if (localTimer == null)
                {
                    return false;
                }

                // El organizador manda sobre su reloj oficial, no sobre tu cronómetro manual.
                organizerAllows = false;
            }

            var carryKey = $"{platform}|{rule.Signature}";
            _journal.Carry.TryGetValue(carryKey, out var carry);
            var seconds = DonationTimeCalculator.Seconds(rule, amount, ref carry);
            _journal.Carry[carryKey] = carry;
            if (seconds <= 0)
            {
                // Aún no llega a un segundo: el sobrante queda guardado para la siguiente.
                _save(_journal);
                return false;
            }

            signedSeconds = rule.Direction == DonationTimeDirection.Add ? seconds : -seconds;
            if (server && organizerAllows)
            {
                pending = new PendingDonation
                {
                    Id = "don-" + Guid.NewGuid().ToString("N"),
                    CreatedUtc = _utcNow(),
                    DeltaSeconds = signedSeconds,
                    Platform = platform,
                    Amount = amount,
                    Gift = gift,
                    GiftCount = count,
                    Viewer = viewer,
                };
                _journal.Pending.Add(pending);
                if (_journal.Pending.Count > MaxPending)
                {
                    var dropped = _journal.Pending.Count - MaxPending;
                    _journal.Pending.RemoveRange(0, dropped);
                    BridgeLog.Warn($"Zeldatón: {dropped} donaciones sin confirmar se descartaron (demasiadas pendientes).");
                }

                Remember(new DonationTimeEntry(pending.Id, pending.CreatedUtc, platform, what, viewer ?? "", pending.DeltaSeconds, null, "Enviando…"));
            }

            // Primero a disco: si HiveShock se cierra ahora, se reenvía al volver a abrirlo.
            _save(_journal);
        }

        var who = string.IsNullOrWhiteSpace(viewer) ? "alguien" : viewer;
        if (pending != null)
        {
            BridgeLog.Info($"Zeldatón: {what} de {who} → {Signed(pending.DeltaSeconds)}");
            _send(pending.ToMessage());
        }

        if (localTimer != null)
        {
            ApplyToLocalTimer(localTimer, platform, what, viewer, signedSeconds);
        }

        Changed?.Invoke();
        return true;
    }

    /// <summary>Suma o resta al cronómetro manual y lo deja en la lista de la página.</summary>
    private void ApplyToLocalTimer(LocalTimerHook timer, DonationPlatform platform, string what, string? viewer, long requested)
    {
        long applied;
        try
        {
            applied = timer.Apply(requested);
        }
        catch (Exception ex)
        {
            BridgeLog.Warn($"Cronómetro manual: no se pudo aplicar la donación ({ex.Message})");
            return;
        }

        var status = applied == requested
            ? "Aplicado al cronómetro manual"
            : applied == 0
                ? "Sin efecto: el cronómetro manual ya está en cero"
                : "Limitado: el cronómetro manual llegó a cero";
        BridgeLog.Info(
            $"Cronómetro manual: {what} de {(string.IsNullOrWhiteSpace(viewer) ? "alguien" : viewer)} → {Signed(applied)}");
        lock (_gate)
        {
            if (applied > 0)
            {
                AddedSeconds += applied;
            }
            else if (applied < 0)
            {
                RemovedSeconds -= applied;
            }

            Remember(new DonationTimeEntry("", _utcNow(), platform, what, viewer ?? "", requested, applied, status));
        }
    }

    /// <summary>Conexión nueva con el servidor: reenvía todo lo pendiente con sus mismos ids.</summary>
    public void OnConnected()
    {
        List<PendingDonation> pending;
        lock (_gate)
        {
            if (DropExpired())
            {
                _save(_journal);
            }

            pending = _journal.Pending.ToList();
        }

        if (pending.Count > 0)
        {
            BridgeLog.Info($"Zeldatón: reenviando {pending.Count} donaciones sin confirmar.");
        }

        foreach (var p in pending)
        {
            _send(p.ToMessage());
        }
    }

    /// <summary>Respuesta del servidor a un mensaje (ACK, TIME_APPLIED o ERROR).</summary>
    public void OnReply(ZeldathonInbound reply)
    {
        if (reply.Id == null || !reply.Id.StartsWith("don-", StringComparison.Ordinal))
        {
            return;
        }

        DonationTimeApplied? applied = null;
        lock (_gate)
        {
            var index = _journal.Pending.FindIndex(p => p.Id == reply.Id);
            if (index < 0)
            {
                return;
            }

            var p = _journal.Pending[index];
            _journal.Pending.RemoveAt(index);
            _save(_journal);

            long? appliedSeconds = null;
            string status;
            switch (reply.Kind)
            {
                case ZeldathonInboundKind.TimeApplied:
                    appliedSeconds = reply.AppliedSeconds;
                    status = LimitText(reply.LimitedBy, reply.AppliedSeconds);
                    break;
                case ZeldathonInboundKind.Ack:
                    // Id repetido: el servidor ya lo había aplicado antes (se perdió la respuesta).
                    status = "Ya aplicado";
                    break;
                default:
                    status = "Rechazado: " + ZeldathonProtocol.Explain(reply.Code, reply.Message);
                    BridgeLog.Warn($"Zeldatón rechazó una donación ({Signed(p.DeltaSeconds)}): {reply.Code} · {reply.Message}");
                    break;
            }

            if (appliedSeconds is > 0)
            {
                AddedSeconds += appliedSeconds.Value;
            }
            else if (appliedSeconds is < 0)
            {
                RemovedSeconds -= appliedSeconds.Value;
            }

            if (appliedSeconds is { } change and not 0)
            {
                applied = new DonationTimeApplied(change, p.DeltaSeconds, reply.LimitedBy, p.CreatedUtc);
            }

            Update(p, appliedSeconds, status);
        }

        Changed?.Invoke();
        if (applied != null)
        {
            Applied?.Invoke(applied);
        }
    }

    /// <summary>"Rose x5 (5 diamantes)", "100 bits".</summary>
    public static string Describe(DonationPlatform platform, long amount, string? gift, int? count)
    {
        var paid = platform == DonationPlatform.TikTok ? $"{amount:N0} diamantes" : $"{amount:N0} bits";
        if (string.IsNullOrWhiteSpace(gift))
        {
            return paid;
        }

        return count is > 1 ? $"{gift} x{count} ({paid})" : $"{gift} ({paid})";
    }

    public static string Signed(long seconds) =>
        seconds == 0 ? "0 s" : (seconds > 0 ? "+" : "−") + DonationTimeCalculator.Format(seconds);

    private static string LimitText(string limitedBy, long applied) => limitedBy switch
    {
        "" => "Aplicado",
        "per_donation" => "Limitado: tope por donación del organizador",
        "daily_limit" => applied == 0 ? "Sin efecto: tope diario del organizador alcanzado" : "Limitado: tope diario del organizador",
        "clock_max" => "Limitado: el reloj ya está al máximo",
        "clock_zero" => "Limitado: el reloj llegó a cero",
        _ => "Limitado",
    };

    /// <summary>Quita pendientes demasiado viejos. True si quitó alguno.</summary>
    private bool DropExpired()
    {
        var limit = _utcNow() - MaxPendingAge;
        var removed = _journal.Pending.RemoveAll(p => p.CreatedUtc < limit);
        if (removed > 0)
        {
            BridgeLog.Warn($"Zeldatón: {removed} donaciones sin confirmar de hace más de {MaxPendingAge.TotalHours:0} h se descartaron.");
        }

        return removed > 0;
    }

    private void Remember(DonationTimeEntry entry)
    {
        _recent.AddFirst(entry);
        while (_recent.Count > MaxRecent)
        {
            _recent.RemoveLast();
        }
    }

    /// <summary>Actualiza la línea de la lista que corresponde a esta donación (o añade una).</summary>
    private void Update(PendingDonation p, long? applied, string status)
    {
        for (var node = _recent.First; node != null; node = node.Next)
        {
            if (node.Value.Id == p.Id)
            {
                node.Value = node.Value with { AppliedSeconds = applied, Status = status };
                return;
            }
        }

        Remember(new DonationTimeEntry(
            p.Id,
            p.CreatedUtc, p.Platform, Describe(p.Platform, p.Amount, p.Gift, p.GiftCount), p.Viewer ?? "",
            p.DeltaSeconds, applied, status));
    }
}
