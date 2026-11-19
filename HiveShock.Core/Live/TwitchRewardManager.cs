using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using HiveShock.Configuration;
using HiveShock.Logging;
using HiveShock.Networking;

namespace HiveShock.Live;

/// <summary>Lo que el router necesita para los canjes de recompensas que gestiona HiveShock.</summary>
public interface ITwitchRedemptionSink
{
    /// <summary>La recompensa la creó HiveShock: se pueden completar sus canjes o devolver los puntos.</summary>
    bool IsManaged(string rewardId);

    /// <summary>
    /// Empieza a seguir un canje admitido. El tracker recibe el resultado de cada efecto; al terminar, el canje se
    /// completa si llegó alguno al juego o se devuelven los puntos si no llegó ninguno.
    /// </summary>
    PlanTracker Begin(TwitchRedemption redemption, string user, int steps);

    /// <summary>Completa el canje o devuelve los puntos ya (pausa, bloqueado, sin efecto, demasiado viejo…).</summary>
    void Resolve(TwitchRedemption redemption, string user, bool fulfilled, string reason);

    /// <summary>El canje sigue con su plan en marcha (se resolverá solo al terminar).</summary>
    bool IsPending(string redemptionId);
}

/// <summary>
/// Recompensas de puntos creadas por HiveShock (fase 2). Twitch solo deja gestionar las recompensas creadas con el
/// mismo Client ID; de esas, HiveShock:
/// <list type="bullet">
/// <item>completa el canje cuando el efecto llega al juego y devuelve los puntos cuando no llega;</item>
/// <item>las pausa mientras no está conectado (o con los efectos en pausa) y reactiva las que tienen efecto;</item>
/// <item>las crea, edita y borra desde la pantalla de Twitch.</item>
/// </list>
/// Las llamadas que cambian el estado van por una cola en orden: «pausar al arrancar» no puede pisar a «reactivar al
/// conectar». Vive con el runtime (no con la conexión) para poder pausarlas y resolver canjes al desconectar.
/// </summary>
public sealed class TwitchRewardManager : ITwitchRedemptionSink, IAsyncDisposable
{
    private const string Api = "https://api.twitch.tv/helix/channel_points/custom_rewards";

    private readonly BridgeOptions _options;
    private readonly HttpClient _http;
    private readonly Channel<Job> _jobs = Channel.CreateUnbounded<Job>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _worker;

    /// <summary>Recompensas de HiveShock → si están pausadas en Twitch (último estado conocido).</summary>
    private readonly ConcurrentDictionary<string, bool> _manageable = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Canjes admitidos cuyo resultado aún no se sabe (id del canje → seguimiento).</summary>
    private readonly ConcurrentDictionary<string, Outstanding> _outstanding = new(StringComparer.Ordinal);

    public TwitchRewardManager(BridgeOptions options, HttpClient? http = null)
    {
        _options = options;
        _http = http ?? HiveShock.Net.SharedHttp.Client;
        _worker = Task.Run(WorkAsync);
    }

    /// <summary>Hay recompensas de HiveShock conocidas (tras <see cref="RefreshManageableAsync"/>).</summary>
    public bool HasManageable => !_manageable.IsEmpty;

    public bool IsManaged(string rewardId) =>
        !string.IsNullOrWhiteSpace(rewardId) && _manageable.ContainsKey(rewardId.Trim());

    public PlanTracker Begin(TwitchRedemption redemption, string user, int steps)
    {
        PlanTracker? tracker = null;
        var entry = new Outstanding(redemption, user, () => tracker!);
        _outstanding[redemption.Id] = entry;
        tracker = new PlanTracker(Math.Max(1, steps), delivered =>
        {
            // Una sola resolución por canje: o la da el plan al terminar, o la desconexión (SettleOutstanding).
            if (_outstanding.TryRemove(redemption.Id, out _))
            {
                Resolve(redemption, user, delivered, delivered ? "" : "el efecto no llegó al juego");
            }
        });
        return tracker;
    }

    public bool IsPending(string redemptionId) => _outstanding.ContainsKey(redemptionId);

    public void Resolve(TwitchRedemption redemption, string user, bool fulfilled, string reason) =>
        Enqueue($"canje de «{redemption.RewardTitle}»", ct => UpdateRedemptionAsync(redemption, user, fulfilled, reason, ct));

    /// <summary>
    /// Al desconectar: los canjes con el plan a medias se resuelven ya (completados si llegó algo, si no se devuelven
    /// los puntos). Al cerrar la app (<paramref name="keepPending"/>) se dejan pendientes en Twitch: sus efectos se
    /// recuperan del registro al volver y el canje se completa entonces.
    /// </summary>
    public void SettleOutstanding(bool keepPending)
    {
        foreach (var (id, entry) in _outstanding.ToArray())
        {
            if (!_outstanding.TryRemove(id, out _) || keepPending)
            {
                continue;
            }

            var delivered = entry.Tracker().Delivered > 0;
            Resolve(entry.Redemption, entry.User, delivered, delivered ? "" : "se desconectó antes de lanzarlo");
        }
    }

    /// <summary>
    /// Deja activas solo las recompensas de HiveShock con efecto en el perfil activo y con el directo en marcha; el
    /// resto, pausadas (así nadie gasta puntos en algo que no va a pasar). Con <paramref name="refresh"/> relee antes
    /// cuáles son de HiveShock.
    /// </summary>
    public void RequestSync(IEnumerable<string> activeRewardIds, bool live, bool refresh = false)
    {
        var active = activeRewardIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        Enqueue("pausar o reactivar", async ct =>
        {
            if (refresh && !await RefreshManageableAsync(ct).ConfigureAwait(false))
            {
                return;
            }

            await SyncPauseAsync(active, live, ct).ConfigureAwait(false);
        });
    }

    /// <summary>Espera (como mucho <paramref name="timeout"/>) a que se envíe lo que está en cola.</summary>
    public async Task FlushAsync(TimeSpan timeout)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_jobs.Writer.TryWrite(new Job("", _ => Task.CompletedTask, done)))
        {
            return;
        }

        await Task.WhenAny(done.Task, Task.Delay(timeout)).ConfigureAwait(false);
    }

    /// <summary>
    /// Relee de Twitch cuáles son las recompensas de HiveShock y su estado. False si no se pudo (sin cuenta, sin el
    /// permiso o canal sin puntos): entonces no se gestiona nada, sin avisar (la pantalla de Twitch ya lo explica).
    /// </summary>
    public async Task<bool> RefreshManageableAsync(CancellationToken ct)
    {
        if (!HasSession(out var broadcaster))
        {
            return false;
        }

        var (status, json) = await SendAsync(
            HttpMethod.Get, $"{Api}?broadcaster_id={Esc(broadcaster)}&only_manageable_rewards=true", null, ct).ConfigureAwait(false);
        if (status != HttpStatusCode.OK)
        {
            return false;
        }

        using var doc = JsonDocument.Parse(json);
        var rewards = TwitchRewardsApi.ParseRewards(doc.RootElement);
        foreach (var gone in _manageable.Keys.Except(rewards.Select(r => r.Id), StringComparer.OrdinalIgnoreCase).ToList())
        {
            _manageable.TryRemove(gone, out _);
        }

        foreach (var reward in rewards)
        {
            _manageable[reward.Id] = reward.IsPaused;
        }

        return true;
    }

    /// <summary>Ids de las recompensas de HiveShock (sin pedir nada a Twitch).</summary>
    public IReadOnlySet<string> ManageableIds() => _manageable.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Canjes que siguen pendientes en Twitch de estas recompensas (para recuperarlos tras un corte).</summary>
    public async Task<IReadOnlyList<TwitchPendingRedemption>> GetPendingAsync(IEnumerable<string> rewardIds, CancellationToken ct)
    {
        var list = new List<TwitchPendingRedemption>();
        if (!HasSession(out var broadcaster))
        {
            return list;
        }

        foreach (var rewardId in rewardIds.Where(IsManaged).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var (status, json) = await SendAsync(
                HttpMethod.Get,
                $"{Api}/redemptions?broadcaster_id={Esc(broadcaster)}&reward_id={Esc(rewardId)}&status=UNFULFILLED&sort=OLDEST&first=50",
                null,
                ct).ConfigureAwait(false);
            if (status == HttpStatusCode.OK)
            {
                using var doc = JsonDocument.Parse(json);
                list.AddRange(TwitchRewardsApi.ParsePendingRedemptions(doc.RootElement));
            }
        }

        return [.. list.OrderBy(p => p.RedeemedAt)];
    }

    /// <summary>Crea una recompensa en el canal (queda gestionada por HiveShock). Lanza con el motivo si Twitch no la acepta.</summary>
    public async Task<TwitchChannelReward> CreateAsync(TwitchRewardSpec spec, CancellationToken ct)
    {
        var broadcaster = RequireSession(spec);
        var (status, json) = await SendAsync(HttpMethod.Post, $"{Api}?broadcaster_id={Esc(broadcaster)}", spec.ToHelixBody(), ct)
            .ConfigureAwait(false);
        var reward = ReadSingle(status, json, "Twitch no creó la recompensa.");
        _manageable[reward.Id] = reward.IsPaused;
        BridgeLog.Info($"Twitch: recompensa «{reward.Title}» creada ({reward.Cost} puntos).");
        return reward with { IsManageable = true };
    }

    /// <summary>Cambia una recompensa de HiveShock. Lanza con el motivo si Twitch no lo acepta.</summary>
    public async Task<TwitchChannelReward> UpdateAsync(string rewardId, TwitchRewardSpec spec, CancellationToken ct)
    {
        var broadcaster = RequireSession(spec);
        var (status, json) = await SendAsync(
            new HttpMethod("PATCH"), $"{Api}?broadcaster_id={Esc(broadcaster)}&id={Esc(rewardId)}", spec.ToHelixBody(), ct)
            .ConfigureAwait(false);
        var reward = ReadSingle(status, json, "Twitch no guardó los cambios de la recompensa.");
        _manageable[reward.Id] = reward.IsPaused;
        return reward with { IsManageable = true };
    }

    /// <summary>Borra una recompensa de HiveShock del canal. Lanza con el motivo si Twitch no lo acepta.</summary>
    public async Task DeleteAsync(string rewardId, CancellationToken ct)
    {
        if (!HasSession(out var broadcaster))
        {
            throw new InvalidOperationException("Entra con tu cuenta de Twitch.");
        }

        var (status, json) = await SendAsync(
            HttpMethod.Delete, $"{Api}?broadcaster_id={Esc(broadcaster)}&id={Esc(rewardId)}", null, ct).ConfigureAwait(false);
        if (status is not (HttpStatusCode.NoContent or HttpStatusCode.OK or HttpStatusCode.NotFound))
        {
            throw new InvalidOperationException(HelixError(json, "Twitch no borró la recompensa."));
        }

        _manageable.TryRemove(rewardId, out _);
    }

    public async ValueTask DisposeAsync()
    {
        _jobs.Writer.TryComplete();
        try
        {
            await _worker.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Twitch no responde: no se retiene el cierre de la app.
        }

        await _cts.CancelAsync().ConfigureAwait(false);
        _cts.Dispose();
    }

    private async Task SyncPauseAsync(IReadOnlySet<string> active, bool live, CancellationToken ct)
    {
        if (!HasSession(out var broadcaster))
        {
            return;
        }

        foreach (var (id, paused) in _manageable.ToArray())
        {
            var want = !(live && active.Contains(id));
            if (paused == want)
            {
                continue;
            }

            var (status, json) = await SendAsync(
                new HttpMethod("PATCH"),
                $"{Api}?broadcaster_id={Esc(broadcaster)}&id={Esc(id)}",
                new Dictionary<string, object> { ["is_paused"] = want },
                ct).ConfigureAwait(false);
            if (status == HttpStatusCode.OK)
            {
                _manageable[id] = want;
            }
            else if (status == HttpStatusCode.NotFound)
            {
                _manageable.TryRemove(id, out _); // la borraron en Twitch
            }
            else
            {
                BridgeLog.Warn($"Twitch recompensas: no se pudo {(want ? "pausar" : "reactivar")} una recompensa ({HelixError(json, status.ToString())}).");
            }
        }
    }

    private async Task UpdateRedemptionAsync(
        TwitchRedemption redemption,
        string user,
        bool fulfilled,
        string reason,
        CancellationToken ct)
    {
        if (!HasSession(out var broadcaster))
        {
            return;
        }

        var (status, json) = await SendAsync(
            new HttpMethod("PATCH"),
            $"{Api}/redemptions?broadcaster_id={Esc(broadcaster)}&reward_id={Esc(redemption.RewardId)}&id={Esc(redemption.Id)}",
            new Dictionary<string, object> { ["status"] = fulfilled ? "FULFILLED" : "CANCELED" },
            ct).ConfigureAwait(false);

        if (status == HttpStatusCode.OK)
        {
            if (!fulfilled)
            {
                var who = string.IsNullOrWhiteSpace(user) ? "el espectador" : user;
                BridgeLog.Info($"Puntos devueltos a {who} por «{redemption.RewardTitle}» ({reason}).");
            }

            return;
        }

        // 404/400: ya no está pendiente (el streamer lo resolvió a mano, o la recompensa se salta la cola en Twitch).
        BridgeLog.Warn(
            $"Twitch recompensas: no se pudo {(fulfilled ? "completar" : "devolver los puntos de")} un canje de " +
            $"«{redemption.RewardTitle}» ({HelixError(json, status.ToString())}).");
    }

    private void Enqueue(string what, Func<CancellationToken, Task> run)
    {
        if (!_jobs.Writer.TryWrite(new Job(what, run, null)))
        {
            BridgeLog.Warn($"Twitch recompensas: no se pudo {what} (cerrando).");
        }
    }

    private async Task WorkAsync()
    {
        await foreach (var job in _jobs.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                await job.Run(_cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                job.Done?.TrySetResult();
                return;
            }
            catch (Exception ex)
            {
                BridgeLog.Warn($"Twitch recompensas: {job.What}: {ex.Message}");
            }
            finally
            {
                job.Done?.TrySetResult();
            }
        }
    }

    private bool HasSession(out string broadcaster)
    {
        broadcaster = _options.TwitchUserId ?? "";
        return broadcaster.Length > 0 &&
               !string.IsNullOrWhiteSpace(_options.TwitchAccessToken) &&
               !string.IsNullOrWhiteSpace(_options.ResolvedTwitchClientId());
    }

    private string RequireSession(TwitchRewardSpec spec)
    {
        if (spec.Validate() is { } problem)
        {
            throw new ArgumentException(problem);
        }

        return HasSession(out var broadcaster) ? broadcaster : throw new InvalidOperationException("Entra con tu cuenta de Twitch.");
    }

    /// <summary>Envía a Helix. Si el token caducó (401) lo renueva una vez y reintenta.</summary>
    private async Task<(HttpStatusCode Status, string Json)> SendAsync(
        HttpMethod method,
        string url,
        object? body,
        CancellationToken ct)
    {
        var clientId = _options.ResolvedTwitchClientId();
        for (var attempt = 0; ; attempt++)
        {
            var refresh = _options.TwitchRefreshToken ?? "";
            using var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.TwitchAccessToken ?? "");
            request.Headers.TryAddWithoutValidation("Client-Id", clientId);
            if (body != null)
            {
                request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            }

            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0 && refresh.Length > 0)
            {
                await TwitchTokenRefresher.RefreshAsync(_options, clientId, refresh, ct).ConfigureAwait(false);
                continue;
            }

            return (response.StatusCode, json);
        }
    }

    private static TwitchChannelReward ReadSingle(HttpStatusCode status, string json, string fallback)
    {
        if (status == HttpStatusCode.OK)
        {
            using var doc = JsonDocument.Parse(json);
            if (TwitchRewardsApi.ParseRewards(doc.RootElement) is [var reward, ..])
            {
                return reward;
            }
        }

        throw new InvalidOperationException(HelixError(json, fallback));
    }

    private static string HelixError(string json, string fallback)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return fallback;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("message", out var m) && m.GetString() is { Length: > 0 } text
                ? text
                : fallback;
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    private static string Esc(string value) => Uri.EscapeDataString(value);

    private sealed record Job(string What, Func<CancellationToken, Task> Run, TaskCompletionSource? Done);

    private sealed record Outstanding(TwitchRedemption Redemption, string User, Func<PlanTracker> Tracker);
}
