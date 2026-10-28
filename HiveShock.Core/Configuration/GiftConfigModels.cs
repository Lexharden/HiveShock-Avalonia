using System.Globalization;
using HiveShock.Input;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace HiveShock.Configuration;

/// <summary>Esquema UI/override de un parámetro numérico declarado en effects.json (_params).</summary>
public sealed class EffectParamDef
{
    public string Key { get; init; } = "";
    public string Label { get; init; } = "";
    /// <summary>hearts = UI en corazones (wire = UI × Scale); raw = misma unidad que el juego.</summary>
    public string Unit { get; init; } = "raw";
    public double Scale { get; init; } = 1;
    public double Min { get; init; }
    public double Max { get; init; } = 100;
    public double Step { get; init; } = 1;

    public double EffectiveScale => Scale > 0 ? Scale : 1;

    public double ClampUi(double uiValue) => Math.Clamp(uiValue, Min, Max);

    public double ToWire(double uiValue) => ClampUi(uiValue) * EffectiveScale;

    public double ToUi(double wireValue) => wireValue / EffectiveScale;

    public double ClampWire(double wireValue) => ToWire(ToUi(wireValue));
}

public sealed class DiamondBracket
{
    public int Min { get; init; }
    public int? Max { get; init; }
    public string Effect { get; init; } = "";
}

public sealed class BitsBracket
{
    public int Min { get; init; }
    public string Effect { get; init; } = "";

    /// <summary>Todos los efectos del tramo; null = solo <see cref="Effect"/>.</summary>
    public EffectPlan? Plan { get; init; }

    public EffectPlan EffectivePlan => Plan ?? EffectPlan.Single(Effect);
}

public sealed class LikesConfig
{
    public int Every { get; init; }
    public string Effect { get; init; } = "";
    public EffectPlan? Plan { get; init; }

    public EffectPlan EffectivePlan => Plan ?? EffectPlan.Single(Effect);
}

public sealed class SimpleEffectConfig
{
    public string Effect { get; init; } = "";
    public EffectPlan? Plan { get; init; }

    public EffectPlan EffectivePlan => Plan ?? EffectPlan.Single(Effect);

    /// <summary>Solo follow: cada usuario dispara el efecto una vez (evita seguir/dejar de seguir en bucle).</summary>
    public bool OncePerUser { get; init; } = true;
}

public sealed class ChatConfig
{
    public bool Enabled { get; init; }
    public string Prefix { get; init; } = "!";
    public int CooldownSec { get; init; } = 30;
    public int GlobalGapSec { get; init; } = 2;
    public Dictionary<string, string> Commands { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Comandos con varios efectos (el valor de <see cref="Commands"/> es el primero).</summary>
    public Dictionary<string, EffectPlan> Plans { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public EffectPlan PlanFor(string command, string effectId) =>
        Plans.TryGetValue(command, out var plan) ? plan : EffectPlan.Single(effectId);
}

public sealed class GiftRuntimeConfig
{
    public IReadOnlyList<GiftRule> Rules { get; init; } = [];
    public IReadOnlyList<GiftGroup> Groups { get; init; } = [];
    public string UnmappedGifts { get; init; } = "ignore";
    public IReadOnlyList<DiamondBracket> GiftsByDiamonds { get; init; } = [];
    public LikesConfig Likes { get; init; } = new();
    public SimpleEffectConfig Follow { get; init; } = new();
    public SimpleEffectConfig Share { get; init; } = new();
    public ChatConfig Chat { get; init; } = new();
    public SimpleEffectConfig TwitchFollow { get; init; } = new();
    public ChatConfig TwitchChat { get; init; } = new();
    public IReadOnlyList<BitsBracket> TwitchBits { get; init; } = [];
    public IReadOnlyList<GiftGoalConfig> Goals { get; init; } = [];
}

/// <summary>Meta del live: varios viewers suman el mismo regalo (no es un combo de uno).</summary>
public sealed class GiftGoalConfig
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public int Need { get; init; } = 2;
    public string Effect { get; init; } = "";
    public EffectPlan? Plan { get; init; }
    public IReadOnlyList<string> Keys { get; init; } = [];
    public bool AlsoInstant { get; init; } = true;
    public bool Repeat { get; init; } = true;

    public EffectPlan EffectivePlan => Plan ?? EffectPlan.Single(Effect);
}
