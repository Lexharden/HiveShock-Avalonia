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

    public ZeldathonService(
        ZeldathonSettings? settings = null,
        IZeldathonTransportFactory? factory = null,
        ZeldathonClientOptions? options = null,
        Func<Uri, CancellationToken, Task<string>>? fetch = null,
        IProcessControl? processes = null)
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
        Client.Connected += Session.OnConnected;
        Client.MessageRejected += (type, code, _) => Session.OnRejected(type, code);
        Clock.Changed += Session.Reconcile;

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
    }

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

    /// <summary>Lee /api/event para saber el presupuesto diario y el estado del evento. Nunca lanza.</summary>
    public async Task RefreshEventAsync(CancellationToken ct = default)
    {
        try
        {
            if (!Settings.TryBuildIngestUri(out var ingest, out _))
            {
                return;
            }

            var builder = new UriBuilder(ingest) { Scheme = ingest.Scheme == "wss" ? "https" : "http" };
            builder.Path = builder.Path[..^"/ingest".Length] + "/api/event";
            var json = await _fetch(builder.Uri, ct).ConfigureAwait(false);
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
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Sin /api/event el cronómetro sigue funcionando con el presupuesto por defecto.
            BridgeLog.Warn($"Zeldathon: no se pudo leer el evento ({ex.Message})");
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
