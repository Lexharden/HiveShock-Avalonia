namespace HiveShock.Configuration;

public sealed class EditableGift
{
    public string Gift { get; set; } = "";
    public List<string> Also { get; set; } = [];
    public string Id { get; set; } = "";
    public string Effect { get; set; } = "";
    public string What { get; set; } = "";
    public int? Diamonds { get; set; }
    /// <summary>Mínimo de unidades del combo (xN) para disparar. 1 = siempre.</summary>
    public int MinCount { get; set; } = 1;
    /// <summary>Si true, un efecto por unidad (x10 → 10). Si false, una vez al cerrar el combo.</summary>
    public bool Each { get; set; }
    /// <summary>Ruta opcional en gifts.json (ej. 5655.webp o gifts-images/custom.png).</summary>
    public string Image { get; set; } = "";
    /// <summary>Si true, aparece en la ventana overlay de OBS / live.</summary>
    public bool Overlay { get; set; }
    /// <summary>Texto en el overlay. Vacío = nombre del gift.</summary>
    public string OverlayText { get; set; } = "";
    /// <summary>Overrides numéricos en unidades wire (p.ej. value=4 = 1 corazón). Vacío = default del efecto.</summary>
    public Dictionary<string, double> Params { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Efectos que se lanzan después de <see cref="Effect"/>, en orden (un regalo puede hacer varias cosas).</summary>
    public List<EditableGiftStep> MoreSteps { get; set; } = [];

    /// <summary>Todos los efectos del regalo como plan: primero <see cref="Effect"/> y luego <see cref="MoreSteps"/>.</summary>
    public EffectPlan ToPlan()
    {
        var steps = new List<EffectStep>(1 + MoreSteps.Count)
        {
            new(Effect, Params.Count > 0 ? new Dictionary<string, double>(Params, StringComparer.OrdinalIgnoreCase) : null),
        };
        foreach (var more in MoreSteps)
        {
            steps.Add(new EffectStep(
                more.Effect,
                more.Params.Count > 0 ? new Dictionary<string, double>(more.Params, StringComparer.OrdinalIgnoreCase) : null,
                EffectPlan.DelayToMs(more.DelaySec)));
        }

        return EffectPlan.FromSteps(steps);
    }

    public string OverlayLabel =>
        string.IsNullOrWhiteSpace(OverlayText) ? Gift : OverlayText;

    public string? ResolvedImagePath
    {
        get
        {
            var direct = GiftImages.ResolvePath(Id, Gift, Image);
            if (direct != null)
            {
                return direct;
            }

            foreach (var alias in Also)
            {
                var byAlias = GiftImages.ResolvePath(null, alias, null);
                if (byAlias != null)
                {
                    return byAlias;
                }
            }

            return null;
        }
    }

    public bool HasImage => ResolvedImagePath != null;
}

/// <summary>Un efecto adicional de un regalo (del segundo en adelante).</summary>
public sealed class EditableGiftStep
{
    public string Effect { get; set; } = "";

    /// <summary>Segundos de espera antes de lanzarlo, contados desde el efecto anterior (0 = enseguida).</summary>
    public double DelaySec { get; set; }

    public Dictionary<string, double> Params { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class EditableChatCommand
{
    public string Word { get; set; } = "";
    public string Effect { get; set; } = "";

    /// <summary>Efectos que se lanzan después del principal, en orden.</summary>
    public List<EditableGiftStep> MoreSteps { get; set; } = [];
}

public sealed class EditableBitsRule
{
    public int Min { get; set; } = 1;
    public string Effect { get; set; } = "";
    public List<EditableGiftStep> MoreSteps { get; set; } = [];
}

/// <summary>Recompensa de puntos del canal de Twitch → efecto (ver <see cref="TwitchRewardRule"/>).</summary>
public sealed class EditableTwitchReward
{
    public string RewardId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Effect { get; set; } = "";
    public List<EditableGiftStep> MoreSteps { get; set; } = [];
}

public sealed class EditableGiftGoal
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public int Need { get; set; } = 2;
    public string Effect { get; set; } = "";
    public List<EditableGiftStep> MoreSteps { get; set; } = [];
    public string GiftsText { get; set; } = "";
    public bool AlsoInstant { get; set; } = true;
    public bool Repeat { get; set; } = true;
}
