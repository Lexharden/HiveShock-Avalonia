using System.Globalization;
using HiveShock.Input;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace HiveShock.Configuration;

public static class GiftKeyNormalizer
{
    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "";
        }

        var formD = name.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var ch in formD)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}

public sealed record GiftRule(
    string Key,
    string Normalized,
    string EffectId,
    string What,
    int MinCount = 1,
    bool Each = false,
    IReadOnlyDictionary<string, double>? Params = null,
    EffectPlan? Plan = null);

public sealed record GiftGroup(IReadOnlyList<string> Names, string EffectId, string What);

/// <summary>Resultado de resolver un regalo TikTok a un efecto + reglas de combo.</summary>
public sealed class GiftMatch
{
    public string EffectId { get; init; } = "";
    /// <summary>Mínimo de unidades del combo (xN) para disparar. 1 = siempre.</summary>
    public int MinCount { get; init; } = 1;
    /// <summary>Si true, encola el efecto una vez por unidad (x10 → 10). Si false, una sola vez al cerrar el combo.</summary>
    public bool Each { get; init; }
    /// <summary>Overrides de parámetros del efecto (valores wire).</summary>
    public IReadOnlyDictionary<string, double>? Params { get; init; }

    /// <summary>Todos los efectos del regalo, en orden. Vacío = solo <see cref="EffectId"/>.</summary>
    public EffectPlan Plan { get; init; } = EffectPlan.Empty;

    /// <summary>Los efectos a lanzar: el plan, o el efecto único de siempre.</summary>
    public EffectPlan EffectivePlan => Plan.IsEmpty ? EffectPlan.Single(EffectId, Params) : Plan;
}
