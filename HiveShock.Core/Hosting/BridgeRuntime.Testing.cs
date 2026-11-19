using HiveShock.Configuration;
using HiveShock.Logging;

namespace HiveShock.Hosting;

/// <summary>Pruebas desde la interfaz (regalos, metas, efectos sueltos) y acciones directas sobre el juego.</summary>
public sealed partial class BridgeRuntime
{
    public void ResetDeathCounter() => DeathCounter.Reset();

    public Task SimulateGoalAsync(string goalId, int units = 1, CancellationToken ct = default)
    {
        var snap = Goals.Snapshots().FirstOrDefault(g =>
            string.Equals(g.Id, goalId, StringComparison.OrdinalIgnoreCase));
        if (snap == null)
        {
            throw new InvalidOperationException("No hay esa meta.");
        }

        var key = snap.Keys.FirstOrDefault() ?? snap.Label;
        var tick = Goals.Contribute(key, key, Math.Max(1, units));
        if (tick == null)
        {
            return Task.CompletedTask;
        }

        BridgeLog.Info($"Meta {tick.Label} {tick.Count}/{tick.Need} (prueba +{tick.Added})");
        if (!tick.Fired || string.IsNullOrWhiteSpace(tick.Effect))
        {
            return Task.CompletedTask;
        }

        BridgeLog.Info($"Meta {tick.Label} -> {tick.Plan.Describe()}");
        return RunPlanForTestAsync(tick.Plan, "Test", "meta", ct);
    }

    /// <summary>Lanza los efectos de un plan en una prueba: respeta las esperas y usa macros, sistema o el juego.</summary>
    private async Task RunPlanForTestAsync(EffectPlan plan, string tester, string tag, CancellationToken ct)
    {
        foreach (var step in plan.Steps)
        {
            if (step.DelayMs > 0)
            {
                await Task.Delay(step.DelayMs, ct).ConfigureAwait(false);
            }

            if (TryRunMacro(step.EffectId, tester, tag) || TryRunSystemEffect(step.EffectId, tester, tag))
            {
                continue;
            }

            var cmd = _effects.Resolve(step.EffectId, tester, step.Params)
                      ?? throw new InvalidOperationException($"Efecto desconocido: {step.EffectId}");
            await _gameClient.SendAsync(cmd, ct).ConfigureAwait(false);
            BridgeLog.Info($"OK prueba {step.EffectId} ({tag})");
        }
    }

    public Task DeleteSaveAsync(CancellationToken ct = default)
    {
        if (!_profile.Info.SupportsDeleteSave)
        {
            throw new InvalidOperationException(
                $"El perfil {_profile.DisplayName} no soporta borrar partida.");
        }

        return _gameClient.SendActionAsync("delete_save", ct);
    }

    public Task RescueAsync(CancellationToken ct = default)
    {
        if (!_profile.Info.SupportsRescue)
        {
            throw new InvalidOperationException(
                $"El perfil {_profile.DisplayName} no soporta rescatar.");
        }

        return _gameClient.SendActionAsync("rescue", ct);
    }

    /// <summary>Dispara el efecto de un mapeo (respeta Mín x / Cada uno / singleton ×1).</summary>
    public async Task TestGiftAsync(
        EditableGift gift,
        int comboCount = 1,
        string testerName = "Test",
        CancellationToken ct = default)
    {
        if (gift == null)
        {
            throw new ArgumentException("El regalo no tiene efecto.");
        }

        var repeat = Math.Max(1, comboCount);
        Overlay.Alert("gift", testerName, gift.Gift, repeat, gift.Id);
        var tick = Goals.Contribute(gift.Gift, gift.Id, repeat);
        if (tick != null)
        {
            BridgeLog.Info($"Meta {tick.Label} {tick.Count}/{tick.Need} ({testerName} +{tick.Added})");
            if (tick.Fired && !tick.Plan.IsEmpty)
            {
                BridgeLog.Info($"Meta {tick.Label} -> {tick.Plan.Describe()}");
                await RunPlanForTestAsync(tick.Plan, testerName, "meta", ct).ConfigureAwait(false);
            }

            if (!tick.AlsoInstant)
            {
                Overlay.Notify(gift.Gift, gift.Id);
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(gift.Effect))
        {
            throw new ArgumentException("El regalo no tiene efecto.");
        }

        var plan = gift.ToPlan();
        foreach (var step in plan.Steps)
        {
            if (!_effects.Contains(step.EffectId))
            {
                throw new ArgumentException($"Efecto desconocido: {step.EffectId}");
            }
        }

        var minCount = Math.Max(1, gift.MinCount);
        if (repeat < minCount)
        {
            BridgeLog.Info($"Prueba omitida: x{repeat} < Mín x{minCount} ({gift.Gift})");
            return;
        }

        bool OnlyOnce(EffectStep step) =>
            _effects.IsSingletonEffect(step.EffectId) || SystemEffectIds.IsSystemEffect(step.EffectId);

        var allOnce = plan.Steps.All(OnlyOnce);
        var times = allOnce ? 1 : (gift.Each ? Math.Min(repeat, 100) : 1);

        BridgeLog.Info(
            $"Prueba {gift.Gift} x{repeat} → {plan.Describe()} " +
            $"({(allOnce ? "1 vez (singleton)" : gift.Each ? $"x{times} cada uno" : "1 vez")})");

        for (var i = 0; i < times; i++)
        {
            foreach (var step in plan.Steps)
            {
                if (i > 0 && OnlyOnce(step))
                {
                    continue;
                }

                if (step.DelayMs > 0)
                {
                    await Task.Delay(step.DelayMs, ct).ConfigureAwait(false);
                }

                if (TryRunMacro(step.EffectId, testerName, "prueba") || TryRunSystemEffect(step.EffectId, testerName, "prueba"))
                {
                    continue;
                }

                var cmd = _effects.Resolve(step.EffectId, testerName, step.Params);
                if (cmd is null)
                {
                    throw new InvalidOperationException($"No se pudo resolver el efecto {step.EffectId}");
                }

                await _gameClient.SendAsync(cmd, ct).ConfigureAwait(false);
                BridgeLog.Info($"OK prueba {step.EffectId} ({i + 1}/{times})");
            }
        }

        Overlay.Notify(gift.Gift, gift.Id);
    }

    public Task SendEffectAsync(string effectId, string testerName = "Test", CancellationToken ct = default)
    {
        if (TryRunMacro(effectId, testerName, "prueba"))
        {
            return Task.CompletedTask;
        }

        var cmd = _effects.Resolve(effectId, testerName)
                  ?? throw new InvalidOperationException($"Efecto desconocido: {effectId}");
        return _gameClient.SendAsync(cmd, ct);
    }
}
