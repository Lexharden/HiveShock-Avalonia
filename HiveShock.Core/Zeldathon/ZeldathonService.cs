using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using HiveShock.Logging;

namespace HiveShock.Zeldathon;

/// <summary>
/// Punto único de Zeldathon dentro de HiveShock: ajustes, reloj oficial y conexión. Vive fuera del
/// puente (se puede ver el cronómetro oficial solo con el token, sin abrir el juego ni el live).
/// </summary>
public sealed class ZeldathonService : IAsyncDisposable
{
    public const long DefaultBudgetSeconds = 4 * 3600;

    private readonly Func<Uri, CancellationToken, Task<string>> _fetch;
    private readonly Timer _tick;
    private readonly IProcessControl _processes;
    private long _chatPending;
    private int _gameMissing;
    private int _ticks;

    public ZeldathonService(
        ZeldathonSettings? settings = null,
        IZeldathonTransportFactory? factory = null,
        ZeldathonClientOptions? options = null,
        Func<Uri, CancellationToken, Task<string>>? fetch = null,
        IProcessControl? processes = null,
        Func<DonationJournal?>? loadDonations = null,
        Action<DonationJournal>? saveDonations = null)
    {
        Settings = settings ?? ZeldathonSettings.Load();
        Clock = new ZeldathonClock();
        var opt = options ?? new ZeldathonClientOptions();
        if (opt.ClientVersion == "hiveshock")
        {
            opt.ClientVersion = "hiveshock/" + (Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "dev");
        }

        Client = new ZeldathonClient(factory ?? new WebSocketTransportFactory(), Clock, opt);
        _fetch = fetch ?? DefaultFetchAsync;
        Client.StateChanged += state => StateChanged?.Invoke(state);

        Session = new ZeldathonSession(Clock, Client.Send, Client.RequestClock);
        Client.GameRunning = () => Session.GameActive;
        Stream = new StreamReporter(() => IsBroadcasting(), Client.Send);
        Client.Connected += Session.OnConnected;
        Client.Connected += Stream.Forget;
        Client.MessageRejected += (type, code, _) => Session.OnRejected(type, code);
        Clock.Changed += Session.Reconcile;

        Donations = new DonationTimeReporter(
            () => Settings.Donations ?? new DonationTimeSettings(),
            () => Settings.Enabled && Settings.HasCredentials,
            Client.Send,
            loadDonations,
            saveDonations);
        Client.Connected += Donations.OnConnected;
        Client.Replied += Donations.OnReply;

        _processes = processes ?? new SystemProcessControl();
        Closer = new GameCloser(
            ct => RequestGameQuit?.Invoke(ct) ?? Task.CompletedTask,
            () => Session.Map.GameProcesses,
            _processes);
        Closer.Finished += (_, message) => Notice?.Invoke(message);
        Client.ForceCloseRequested += () => _ = Closer.CloseAsync();

        // Cada 5 s: reintenta iniciar la sesión (el evento puede empezar después), manda el chat contado
        // y comprueba que el juego sigue abierto.
        _tick = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    /// <summary>Directo y espectadores del corredor → STREAM_STATE.</summary>
    public StreamReporter Stream { get; }

    /// <summary>Regalos de TikTok y bits de Twitch → tiempo de la carrera (TIME_DONATION).</summary>
    public DonationTimeReporter Donations { get; }

    /// <summary>Dice si TikTok o Twitch están en directo. La pone quien conoce los canales del live.</summary>
    public Func<bool> IsBroadcasting { get; set; } = () => false;

    /// <summary>Cierra el juego cuando el servidor lo ordena (pide cierre limpio y, si hace falta, termina el proceso).</summary>
    public GameCloser Closer { get; }

    /// <summary>Pide al juego que se cierre limpiamente (acción <c>quit_game</c>). La pone quien conoce la conexión con el juego.</summary>
    public Func<CancellationToken, Task>? RequestGameQuit { get; set; }

    /// <summary>Aviso importante para el usuario (p. ej. "se acabó el tiempo, se cerró el juego").</summary>
    public event Action<string>? Notice;

    /// <summary>Cuenta mensajes de chat (TikTok y Twitch) para la métrica de HiveShock de la web.</summary>
    public void CountChat(int count = 1)
    {
        if (count > 0)
        {
            Interlocked.Add(ref _chatPending, count);
        }
    }

    /// <summary>Un paso del ciclo de 5 s. Público para poder probarlo sin esperar.</summary>
    public void Tick()
    {
        Session.Reconcile();
        FlushChat();
        WatchGame();
        RefreshStream();

        // Cada ~5 min: por si el organizador cambió el catálogo o las reglas del evento.
        if (++_ticks % 60 == 0 && Client.State == ZeldathonConnectionState.Connected)
        {
            _ = RefreshEventAsync();
            _ = RefreshCatalogAsync();
        }
    }

    /// <summary>Manda el estado del directo si cambió (se llama al cambiar un canal y cada 5 s).</summary>
    public void RefreshStream() => Stream.Tick(Client.State == ZeldathonConnectionState.Connected);

    private void FlushChat()
    {
        if (Client.State != ZeldathonConnectionState.Connected)
        {
            return;
        }

        var count = Interlocked.Exchange(ref _chatPending, 0);
        if (count > 0)
        {
            Client.Send(ZeldathonProtocol.Message("CHAT_EVENT", fields: new JsonObject { ["count"] = (int)Math.Min(count, 1000) }));
            if (count > 1000)
            {
                Interlocked.Add(ref _chatPending, count - 1000);
            }
        }
    }

    /// <summary>
    /// Si el juego se cierra sin avisar (ventana, fallo), el puerto de eventos no lo cuenta: se comprueba
    /// el proceso y, tras dos revisiones seguidas sin él, se da la sesión por terminada.
    /// </summary>
    private void WatchGame()
    {
        var names = Session.Map.GameProcesses;
        if (!Session.GameActive || names.Count == 0)
        {
            _gameMissing = 0;
            return;
        }

        if (_processes.Find(names).Count > 0)
        {
            _gameMissing = 0;
            return;
        }

        if (++_gameMissing >= 2)
        {
            _gameMissing = 0;
            using var doc = JsonDocument.Parse("""{"event":"game_session","state":"exited"}""");
            Session.OnGameEvent("game_session", doc.RootElement.Clone());
            BridgeLog.Info("Zeldathon: el juego se cerró; sesión terminada.");
        }
    }

    public ZeldathonSettings Settings { get; }
    public ZeldathonClock Clock { get; }
    public ZeldathonClient Client { get; }

    /// <summary>Estado del juego → mensajes de ingesta (progreso, ítems, jefes, stats).</summary>
    public ZeldathonSession Session { get; }

    /// <summary>Carga la traducción juego → catálogo del perfil (zeldathon.json en su carpeta).</summary>
    public void LoadMap(string profileDirectory, string profileId)
    {
        Session.Map = ZeldathonMap.LoadForProfile(profileDirectory, profileId);
        if (Session.Map.IsEmpty)
        {
            BridgeLog.Info("Zeldathon: este perfil no tiene zeldathon.json; no se enviará progreso del juego.");
        }
    }

    /// <summary>Presupuesto diario del evento (el servidor lo dice en /api/event; por defecto 4 h).</summary>
    public long BudgetSeconds { get; private set; } = DefaultBudgetSeconds;

    /// <summary>Ítems y objetivos que acepta el servidor (descargados al conectar; antes, el de fábrica).</summary>
    public ZeldathonCatalog Catalog => Session.Catalog;

    /// <summary>Estado del evento según el servidor ("upcoming", "live", "paused", "finished") o vacío.</summary>
    public string EventStatus { get; private set; } = "";

    public ZeldathonConnectionState State => Client.State;

    public event Action<ZeldathonConnectionState>? StateChanged;

    /// <summary>Conecta con lo guardado en <see cref="Settings"/>. Devuelve un error legible o null si arrancó.</summary>
    public string? Connect()
    {
        if (string.IsNullOrWhiteSpace(Settings.Token))
        {
            return "Falta el token del corredor.";
        }

        if (!Settings.TryBuildIngestUri(out var uri, out var error))
        {
            return error;
        }

        Client.Start(uri, Settings.Token.Trim());
        _ = RefreshEventAsync();
        _ = RefreshCatalogAsync();
        BridgeLog.Info($"Zeldathon: conectando a {uri.Host}");
        return null;
    }

    public void Disconnect()
    {
        Client.Stop();
        BridgeLog.Info("Zeldathon desconectado");
    }

    /// <summary>Arranque de HiveShock: conecta sola si el usuario lo dejó así.</summary>
    public void AutoConnectIfConfigured()
    {
        if (Settings.Enabled && Settings.AutoConnect && Settings.HasCredentials)
        {
            var error = Connect();
            if (error != null)
            {
                BridgeLog.Warn($"Zeldathon: {error}");
            }
        }
    }

    private Uri? ApiUri(string path)
    {
        if (!Settings.TryBuildIngestUri(out var ingest, out _))
        {
            return null;
        }

        var builder = new UriBuilder(ingest) { Scheme = ingest.Scheme == "wss" ? "https" : "http" };
        builder.Path = builder.Path[..^"/ingest".Length] + path;
        return builder.Uri;
    }

    /// <summary>
    /// Lee /api/event: presupuesto diario, estado y los objetivos que exige para terminar. Nunca lanza.
    /// </summary>
    public async Task RefreshEventAsync(CancellationToken ct = default)
    {
        try
        {
            if (ApiUri("/api/event") is not { } uri)
            {
                return;
            }

            var json = await _fetch(uri, ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("dailyBudgetSeconds", out var b) && b.TryGetInt64(out var seconds) && seconds > 0)
            {
                BudgetSeconds = seconds;
            }

            if (root.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.String)
            {
                EventStatus = st.GetString() ?? "";
            }

            if (root.TryGetProperty("donationTime", out var donation))
            {
                Donations.Policy = DonationTimePolicy.FromJson(donation);
            }

            if (root.TryGetProperty("rules", out var rules) &&
                rules.TryGetProperty("requiredObjectiveIds", out var req) && req.ValueKind == JsonValueKind.Array)
            {
                var ids = req.EnumerateArray().Select(e => e.GetString()).OfType<string>().Where(x => x.Length > 0).ToList();
                if (ids.Count > 0)
                {
                    Session.RequiredObjectives = ids;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Sin /api/event el cronómetro sigue funcionando con el presupuesto por defecto.
            BridgeLog.Warn($"Zeldathon: no se pudo leer el evento ({ex.Message})");
        }
    }

    /// <summary>
    /// Descarga /api/catalog (lo edita el organizador). Si falla se conserva el que ya había (el de fábrica
    /// al principio). Nunca lanza.
    /// </summary>
    public async Task RefreshCatalogAsync(CancellationToken ct = default)
    {
        try
        {
            if (ApiUri("/api/catalog") is not { } uri)
            {
                return;
            }

            var catalog = ZeldathonCatalog.Parse(await _fetch(uri, ct).ConfigureAwait(false));
            if (catalog.Version != Session.Catalog.Version)
            {
                Session.Catalog = catalog;
                BridgeLog.Info($"Zeldathon: catálogo actualizado ({catalog.Items.Count} ítems, {catalog.Objectives.Count} objetivos)");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            BridgeLog.Warn($"Zeldathon: no se pudo leer el catálogo ({ex.Message})");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _tick.DisposeAsync().ConfigureAwait(false);
        await Client.DisposeAsync().ConfigureAwait(false);
    }

    private static async Task<string> DefaultFetchAsync(Uri uri, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        return await http.GetStringAsync(uri, ct).ConfigureAwait(false);
    }
}
