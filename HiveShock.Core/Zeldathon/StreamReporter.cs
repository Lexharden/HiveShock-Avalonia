using System.Text.Json.Nodes;

namespace HiveShock.Zeldathon;

/// <summary>
/// Cuenta al servidor si el corredor está en directo y cuántos espectadores tiene (STREAM_STATE). Se manda
/// al cambiar el estado de directo y, si solo cambian los espectadores, como mucho cada 30 s.
/// </summary>
public sealed class StreamReporter
{
    public static readonly TimeSpan ViewersEvery = TimeSpan.FromSeconds(30);

    private readonly object _gate = new();
    private readonly Func<bool> _isLive;
    private readonly Action<ZeldathonOutbound> _send;
    private readonly Func<long> _nowMs;
    private int? _viewers;
    private (bool Live, int? Viewers)? _sent;
    private long _sentAtMs;

    public StreamReporter(Func<bool> isLive, Action<ZeldathonOutbound> send, Func<long>? nowMs = null)
    {
        _isLive = isLive;
        _send = send;
        _nowMs = nowMs ?? (() => Environment.TickCount64);
    }

    /// <summary>Espectadores actuales (TikTok los manda cada pocos segundos). Null = no se sabe.</summary>
    public void SetViewers(int? viewers)
    {
        lock (_gate)
        {
            _viewers = viewers is { } v ? Math.Max(0, v) : null;
        }
    }

    /// <summary>Nueva conexión con el servidor: hay que contarle el estado otra vez.</summary>
    public void Forget()
    {
        lock (_gate)
        {
            _sent = null;
        }
    }

    /// <summary>Manda el estado si toca. <paramref name="connected"/> falso = no se manda ni se da por enviado.</summary>
    public void Tick(bool connected)
    {
        if (!connected)
        {
            return;
        }

        lock (_gate)
        {
            var live = _isLive();
            int? viewers = live ? _viewers : null;
            var now = _nowMs();
            if (_sent is { } last)
            {
                if (last.Live == live && last.Viewers == viewers)
                {
                    return;
                }

                // Solo cambian los espectadores: sin prisa, para no llenar el servidor de mensajes.
                if (last.Live == live && now - _sentAtMs < ViewersEvery.TotalMilliseconds)
                {
                    return;
                }
            }

            var fields = new JsonObject { ["live"] = live };
            if (viewers is { } v)
            {
                fields["viewers"] = v;
            }

            _send(ZeldathonProtocol.Message("STREAM_STATE", fields: fields));
            _sent = (live, viewers);
            _sentAtMs = now;
        }
    }
}
