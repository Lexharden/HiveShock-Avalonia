using HiveShock.Hosting;

namespace HiveShock.Configuration;

/// <summary>Un efecto dentro de un <see cref="EffectPlan"/>: qué efecto, con qué parámetros y cuánto esperar antes.</summary>
/// <param name="EffectId">Id del efecto (del juego, macro de teclas o del sistema).</param>
/// <param name="Params">Parámetros que sustituyen a los del efecto (unidades del juego). Null = los del efecto.</param>
/// <param name="DelayMs">Espera antes de lanzarlo, contada desde que se lanzó el paso anterior. 0 = enseguida.</param>
public sealed record EffectStep(string EffectId, IReadOnlyDictionary<string, double>? Params = null, int DelayMs = 0);

/// <summary>
/// Lo que hace un disparador (regalo, comando…): una lista ordenada de efectos. Con un solo paso se comporta
/// exactamente igual que el «efecto único» de siempre.
/// </summary>
public sealed class EffectPlan
{
    /// <summary>Máximo de efectos por disparador.</summary>
    public const int MaxSteps = 8;

    /// <summary>Máxima espera por paso, en segundos.</summary>
    public const int MaxDelaySec = 30;

    /// <summary>Tope de efectos que puede encolar un solo regalo (combo × pasos), para no inundar la cola.</summary>
    public const int MaxTotalSteps = 200;

    public static readonly EffectPlan Empty = new([]);

    private EffectPlan(IReadOnlyList<EffectStep> steps) => Steps = steps;

    public IReadOnlyList<EffectStep> Steps { get; }

    public bool IsEmpty => Steps.Count == 0;

    /// <summary>Primer efecto (el que ven las versiones antiguas y las validaciones sencillas).</summary>
    public string EffectId => Steps.Count > 0 ? Steps[0].EffectId : "";

    public static EffectPlan Single(string effectId, IReadOnlyDictionary<string, double>? parameters = null) =>
        string.IsNullOrWhiteSpace(effectId) ? Empty : new EffectPlan([new EffectStep(effectId.Trim(), parameters)]);

    /// <summary>
    /// Normaliza la lista: quita vacíos, acota pasos y esperas, y deja los efectos del sistema (reiniciar el equipo)
    /// al final, porque después de reiniciar no llegaría a ejecutarse nada más.
    /// </summary>
    public static EffectPlan FromSteps(IEnumerable<EffectStep> steps)
    {
        var clean = steps
            .Where(s => !string.IsNullOrWhiteSpace(s.EffectId))
            .Select(s => s with
            {
                EffectId = s.EffectId.Trim(),
                DelayMs = Math.Clamp(s.DelayMs, 0, MaxDelaySec * 1000),
            })
            .Take(MaxSteps)
            .ToList();
        if (clean.Count == 0)
        {
            return Empty;
        }

        var ordered = clean.Where(s => !SystemEffectIds.IsSystemEffect(s.EffectId))
            .Concat(clean.Where(s => SystemEffectIds.IsSystemEffect(s.EffectId)))
            .ToList();
        return new EffectPlan(ordered);
    }

    /// <summary>«a + b + c», para el registro.</summary>
    public string Describe() => string.Join(" + ", Steps.Select(s => s.EffectId));

    /// <summary>Convierte segundos (admite decimales) a milisegundos acotados.</summary>
    public static int DelayToMs(double seconds) =>
        double.IsNaN(seconds) ? 0 : (int)Math.Round(Math.Clamp(seconds, 0, MaxDelaySec) * 1000);
}

/// <summary>Un efecto ya preparado para encolar, con el motivo que se verá en el registro.</summary>
public sealed record PlannedEffect(EffectStep Step, string Reason);

/// <summary>Convierte un plan y el tamaño del combo en la lista exacta de efectos que hay que encolar.</summary>
public static class PlanExpander
{
    /// <summary>Efectos que solo se lanzan una vez por regalo aunque el combo sea grande (reiniciar el equipo, singleton).</summary>
    public static bool RunsOnlyOnce(EffectCatalog effects, EffectStep step) =>
        effects.IsSingletonEffect(step.EffectId) || SystemEffectIds.IsSystemEffect(step.EffectId);

    /// <summary>
    /// Con <paramref name="each"/> el plan completo se repite por cada regalo del combo (hasta 100): A B C A B C.
    /// Los efectos «una sola vez» salen solo en la primera vuelta. Nunca devuelve más de
    /// <see cref="EffectPlan.MaxTotalSteps"/> efectos.
    /// </summary>
    public static List<PlannedEffect> Expand(
        EffectPlan plan,
        int repeat,
        bool each,
        string giftLabel,
        Func<EffectStep, bool> onlyOnce,
        out int rounds,
        out bool allOnce,
        out bool truncated)
    {
        const int maxRepeats = 100;
        var steps = plan.Steps;
        allOnce = steps.Count > 0 && steps.All(onlyOnce);
        rounds = allOnce ? 1 : (each ? Math.Min(Math.Max(1, repeat), maxRepeats) : 1);
        truncated = false;

        var planned = new List<PlannedEffect>(Math.Min(rounds * steps.Count, EffectPlan.MaxTotalSteps));
        for (var i = 0; i < rounds && !truncated; i++)
        {
            for (var k = 0; k < steps.Count; k++)
            {
                if (i > 0 && onlyOnce(steps[k]))
                {
                    continue;
                }

                if (planned.Count >= EffectPlan.MaxTotalSteps)
                {
                    truncated = true;
                    break;
                }

                var reason = rounds > 1
                    ? $"Regalo {giftLabel} x{repeat} ({i + 1}/{rounds})"
                    : $"Regalo {giftLabel} x{repeat}";
                if (steps.Count > 1)
                {
                    reason += $" [{k + 1}/{steps.Count}]";
                }

                planned.Add(new PlannedEffect(steps[k], reason));
            }
        }

        return planned;
    }
}
