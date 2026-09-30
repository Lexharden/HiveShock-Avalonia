using System.Threading.Channels;
using HiveShock.Logging;

namespace HiveShock.Zeldathon;

public enum ZeldathonConnectionState
{
    Stopped,
    Connecting,
    Connected,
    Reconnecting,
    /// <summary>El servidor no aceptó el token: no se reintenta hasta que se corrija.</summary>
    AuthFailed,
    /// <summary>Otra conexión con el mismo token tomó el lugar: no se pelea por ella.</summary>
    Replaced,
}

public sealed class ZeldathonClientOptions
{
    /// <summary>El servidor da por caído a quien pasa 20 s sin latido; con 8 s hay margen de sobra.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(8);

    public TimeSpan MinBackoff { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Tope de mensajes pendientes (cola y sin confirmar); si se pasa se descartan los más viejos.</summary>
    public int MaxPending { get; set; } = 500;

    public string ClientVersion { get; set; } = "hiveshock";

    /// <summary>Pausa entre reintentos y latidos; se sustituye en las pruebas.</summary>
    public Func<TimeSpan, CancellationToken, Task>? Delay { get; set; }

    public Random? Random { get; set; }
}

/// <summary>
/// Cliente de ingesta de Zeldathon: mantiene la conexión (con reconexión y latidos), entrega el reloj
/// oficial a <see cref="ZeldathonClock"/> y envía los mensajes del juego. Todo mensaje lleva id y queda
/// pendiente hasta que el servidor lo confirma, así un reintento tras una caída nunca aplica algo dos veces.
/// </summary>
public sealed class ZeldathonClient : IAsyncDisposable
{
    private static readonly TimeSpan StableAfter = TimeSpan.FromSeconds(30);

    private readonly IZeldathonTransportFactory _factory;
    private readonly ZeldathonClock _clock;
    private readonly ZeldathonClientOptions _options;
    private readonly Random _random;
    private readonly object _gate = new();
    private readonly Channel<ZeldathonOutbound> _outbox;
    private readonly Dictionary<string, ZeldathonOutbound> _inflight = new();
    private readonly Dictionary<string, long> _heartbeatSentAt = new();
    private CancellationTokenSource? _cts;
    private Task? _run;
    private long _heartbeatSeq;
    private long _sent;
    private long _acked;
    private long _rejected;

    public ZeldathonClient(IZeldathonTransportFactory factory, ZeldathonClock clock, ZeldathonClientOptions? options = null)
    {
        _factory = factory;
        _clock = clock;
        _options = options ?? new ZeldathonClientOptions();
        _random = _options.Random ?? Random.Shared;
        _outbox = Channel.CreateBounded<ZeldathonOutbound>(new BoundedChannelOptions(Math.Max(16, _options.MaxPending))
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
    }

    public ZeldathonConnectionState State { get; private set; } = ZeldathonConnectionState.Stopped;

    /// <summary>Último motivo legible de fallo o rechazo (vacío si todo va bien).</summary>
    public string LastError { get; private set; } = "";

    public long Sent => Interlocked.Read(ref _sent);
    public long Acked => Interlocked.Read(ref _acked);
    public long Rejected => Interlocked.Read(ref _rejected);

    /// <summary>Dice si el juego está abierto, para el latido (null = no se sabe).</summary>
    public Func<bool?>? GameRunning { get; set; }

    public event Action<ZeldathonConnectionState>? StateChanged;

    /// <summary>Se dispara en cada conexión nueva, ya enviado el HELLO: momento de reenviar el estado completo.</summary>
    public event Action? Connected;

    /// <summary>El servidor ordena cerrar el juego (se acabó el tiempo del día).</summary>
    public event Action? ForceCloseRequested;

    /// <summary>El servidor rechazó un mensaje: (tipo del mensaje o "", código, texto legible).</summary>
    public event Action<string, string, string>? MessageRejected;

    /// <summary>
    /// Respuesta del servidor a un mensaje con id: ACK, TIME_APPLIED o ERROR. Para quien necesita saber
    /// qué pasó con un mensaje concreto (p. ej. las donaciones pendientes).
    /// </summary>
    public event Action<ZeldathonInbound>? Replied;

    public bool IsRunning => _run is { IsCompleted: false };

    public void Start(Uri uri, string token)
    {
        Stop();
        var cts = new CancellationTokenSource();
        _cts = cts;
        _run = Task.Run(() => RunAsync(uri, token, cts.Token));
    }

    public void Stop()
    {
        var cts = _cts;
        var run = _run;
        _cts = null;
        _run = null;
        if (cts == null)
        {
            return;
        }

        cts.Cancel();
        try
        {
            run?.Wait(TimeSpan.FromSeconds(3));
        }
        catch
        {
            // ignore
        }

        cts.Dispose();
        _clock.MarkDisconnected();
        SetState(ZeldathonConnectionState.Stopped);
    }

    /// <summary>Encola un mensaje. Nunca bloquea ni lanza; sin conexión queda esperando (acotado).</summary>
    public void Send(ZeldathonOutbound message)
    {
        lock (_gate)
        {
            _inflight[message.Id] = message;
            while (_inflight.Count > _options.MaxPending)
            {
                _inflight.Remove(_inflight.Keys.First());
            }
        }

        _outbox.Writer.TryWrite(message);
    }

    /// <summary>Pide el reloj oficial ya (un latido extra), sin esperar al siguiente.</summary>
    public void RequestClock() =>
        Send(ZeldathonProtocol.Heartbeat($"hb-q-{Interlocked.Increment(ref _heartbeatSeq)}", GameRunning?.Invoke()));

    public async ValueTask DisposeAsync() => await Task.Run(Stop).ConfigureAwait(false);

    private async Task RunAsync(Uri uri, string token, CancellationToken ct)
    {
        var backoff = _options.MinBackoff;
        var first = true;
        while (!ct.IsCancellationRequested)
        {
            SetState(first ? ZeldathonConnectionState.Connecting : ZeldathonConnectionState.Reconnecting);
            first = false;
            IZeldathonTransport? transport = null;
            var connectedAt = 0L;
            try
            {
                transport = _factory.Create();
                await transport.ConnectAsync(uri, token, ct).ConfigureAwait(false);
                connectedAt = Environment.TickCount64;
                LastError = "";
                SetState(ZeldathonConnectionState.Connected);
                await SessionAsync(transport, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (ZeldathonAuthException ex)
            {
                LastError = "El servidor no aceptó el token. Revísalo en la página de Zeldathon.";
                BridgeLog.Warn($"Zeldathon: {ex.Message}");
                _clock.MarkDisconnected();
                SetState(ZeldathonConnectionState.AuthFailed);
                return;
            }
            catch (Exception ex)
            {
                LastError = $"Sin conexión: {ex.Message}";
            }
            finally
            {
                if (transport != null)
                {
                    await transport.DisposeAsync().ConfigureAwait(false);
                }
            }

            _clock.MarkDisconnected();
            if (State == ZeldathonConnectionState.Replaced)
            {
                return;
            }

            if (connectedAt != 0 && Environment.TickCount64 - connectedAt >= StableAfter.TotalMilliseconds)
            {
                backoff = _options.MinBackoff;
            }

            SetState(ZeldathonConnectionState.Reconnecting);
            try
            {
                await DelayAsync(Jitter(backoff), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            backoff = TimeSpan.FromMilliseconds(Math.Min(backoff.TotalMilliseconds * 2, _options.MaxBackoff.TotalMilliseconds));
        }
    }

    private async Task SessionAsync(IZeldathonTransport transport, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = linked.Token;

        var hello = ZeldathonProtocol.Hello(_options.ClientVersion);
        await transport.SendAsync(hello.Json, token).ConfigureAwait(false);
        Connected?.Invoke();

        var receive = ReceiveLoopAsync(transport, token);
        var send = SendLoopAsync(transport, token);
        var beat = HeartbeatLoopAsync(transport, token);
        var finished = await Task.WhenAny(receive, send, beat).ConfigureAwait(false);
        linked.Cancel();
        try
        {
            await Task.WhenAll(receive, send, beat).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // el resto se cancela a propósito
        }

        await finished.ConfigureAwait(false);
    }

    private async Task ReceiveLoopAsync(IZeldathonTransport transport, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var text = await transport.ReceiveAsync(ct).ConfigureAwait(false);
            if (text == null)
            {
                return;
            }

            if (Handle(ZeldathonProtocol.Parse(text)))
            {
                return;
            }
        }
    }

    /// <summary>Procesa un mensaje del servidor. True si la sesión debe terminar.</summary>
    private bool Handle(ZeldathonInbound msg)
    {
        switch (msg.Kind)
        {
            case ZeldathonInboundKind.Clock when msg.Clock != null:
                _clock.Update(msg.Clock, TakeRoundTrip(msg.Id));
                Forget(msg.Id);
                return false;
            case ZeldathonInboundKind.Ack:
                Interlocked.Increment(ref _acked);
                Forget(msg.Id);
                Replied?.Invoke(msg);
                return false;
            case ZeldathonInboundKind.TimeApplied:
                if (msg.Clock != null)
                {
                    _clock.Update(msg.Clock);
                }

                Interlocked.Increment(ref _acked);
                Forget(msg.Id);
                Replied?.Invoke(msg);
                return false;
            case ZeldathonInboundKind.ForceClose:
                BridgeLog.Warn("Zeldathon: el tiempo del día se agotó, hay que cerrar el juego.");
                ForceCloseRequested?.Invoke();
                return false;
            case ZeldathonInboundKind.Error:
                return HandleError(msg);
            default:
                return false;
        }
    }

    private bool HandleError(ZeldathonInbound msg)
    {
        string type;
        lock (_gate)
        {
            type = msg.Id != null && _inflight.TryGetValue(msg.Id, out var m) ? m.Type : "";
        }

        // Rechazado = el servidor ya lo miró: reenviarlo no cambiaría nada.
        Forget(msg.Id);
        Interlocked.Increment(ref _rejected);
        var text = ZeldathonProtocol.Explain(msg.Code, msg.Message);
        LastError = text;
        BridgeLog.Warn($"Zeldathon rechazó {(type.Length > 0 ? type : "un mensaje")}: {msg.Code} · {msg.Message}");
        MessageRejected?.Invoke(type, msg.Code, text);
        if (msg.Id != null)
        {
            Replied?.Invoke(msg);
        }

        if (msg.Code == "replaced")
        {
            SetState(ZeldathonConnectionState.Replaced);
            return true;
        }

        return false;
    }

    private async Task SendLoopAsync(IZeldathonTransport transport, CancellationToken ct)
    {
        // Lo encolado antes de esta conexión ya está también en _inflight: se descarta de la cola y se
        // reenvía desde ahí (con los mismos ids, así un duplicado nunca se aplica dos veces).
        while (_outbox.Reader.TryRead(out _))
        {
        }

        List<ZeldathonOutbound> replay;
        lock (_gate)
        {
            replay = _inflight.Values.ToList();
        }

        var replayed = replay.Select(m => m.Id).ToHashSet();
        foreach (var message in replay)
        {
            await transport.SendAsync(message.Json, ct).ConfigureAwait(false);
            Interlocked.Increment(ref _sent);
        }

        await foreach (var message in _outbox.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            // Encolado entre el vaciado y la copia de arriba: ya se envió.
            if (replayed.Remove(message.Id))
            {
                continue;
            }

            await transport.SendAsync(message.Json, ct).ConfigureAwait(false);
            Interlocked.Increment(ref _sent);
        }
    }

    private async Task HeartbeatLoopAsync(IZeldathonTransport transport, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await DelayAsync(_options.HeartbeatInterval, ct).ConfigureAwait(false);
            var id = $"hb-{Interlocked.Increment(ref _heartbeatSeq)}";
            lock (_gate)
            {
                _heartbeatSentAt[id] = Environment.TickCount64;
                while (_heartbeatSentAt.Count > 8)
                {
                    _heartbeatSentAt.Remove(_heartbeatSentAt.Keys.First());
                }
            }

            await transport.SendAsync(ZeldathonProtocol.Heartbeat(id, GameRunning?.Invoke()).Json, ct).ConfigureAwait(false);
        }
    }

    private long TakeRoundTrip(string? id)
    {
        if (id == null)
        {
            return 0;
        }

        lock (_gate)
        {
            if (_heartbeatSentAt.Remove(id, out var sentAt))
            {
                return Math.Max(0, Environment.TickCount64 - sentAt);
            }
        }

        return 0;
    }

    private void Forget(string? id)
    {
        if (id == null)
        {
            return;
        }

        lock (_gate)
        {
            _inflight.Remove(id);
        }
    }

    private TimeSpan Jitter(TimeSpan delay)
    {
        var factor = 0.8 + _random.NextDouble() * 0.4;
        return TimeSpan.FromMilliseconds(delay.TotalMilliseconds * factor);
    }

    private Task DelayAsync(TimeSpan delay, CancellationToken ct) =>
        _options.Delay != null ? _options.Delay(delay, ct) : Task.Delay(delay, ct);

    private void SetState(ZeldathonConnectionState state)
    {
        if (State == state)
        {
            return;
        }

        State = state;
        StateChanged?.Invoke(state);
    }
}
