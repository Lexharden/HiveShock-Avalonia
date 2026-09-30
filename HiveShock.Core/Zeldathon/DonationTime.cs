using System.Text.Json;
using System.Text.Json.Serialization;

namespace HiveShock.Zeldathon;

/// <summary>Qué hace una donación con el tiempo de la carrera.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DonationTimeDirection>))]
public enum DonationTimeDirection
{
    Add,
    Remove,
}

/// <summary>De dónde viene una donación. Cada una se paga en su moneda (diamantes o bits).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DonationPlatform>))]
public enum DonationPlatform
{
    TikTok,
    Twitch,
}

/// <summary>
/// Tarifa del streamer para una plataforma: "cada <see cref="Units"/> diamantes (o bits) son
/// <see cref="Seconds"/> segundos", que suman o restan según <see cref="Direction"/>.
/// </summary>
public sealed class DonationTimeRule
{
    public bool Enabled { get; set; } = true;

    public DonationTimeDirection Direction { get; set; } = DonationTimeDirection.Add;

    public int Units { get; set; } = 1;

    public int Seconds { get; set; } = 1;

    /// <summary>Donaciones de menos de esto (diamantes o bits) no cambian el tiempo.</summary>
    public int MinUnits { get; set; } = 1;

    /// <summary>Tope propio por donación en segundos (0 = sin tope; el organizador tiene el suyo).</summary>
    public int MaxSecondsPerDonation { get; set; }

    /// <summary>Identifica la tarifa: si cambia, el sobrante acumulado de la anterior ya no vale.</summary>
    [JsonIgnore]
    public string Signature => $"{Units}:{Seconds}";
}

/// <summary>Ajustes de "tiempo por donaciones" del streamer (se guardan con la conexión de Zeldatón).</summary>
public sealed class DonationTimeSettings
{
    public bool Enabled { get; set; }

    public DonationTimeRule TikTok { get; set; } = new();

    public DonationTimeRule Twitch { get; set; } = new();

    public DonationTimeRule For(DonationPlatform platform) => platform == DonationPlatform.TikTok ? TikTok : Twitch;
}

/// <summary>Límites del organizador (<c>donationTime</c> de /api/event). El servidor los aplica siempre.</summary>
public sealed record DonationTimePolicy(
    bool Enabled,
    bool AllowAdd,
    bool AllowRemove,
    long MaxSecondsPerDonation,
    long MaxAddedSecondsPerDay,
    long MaxRemovedSecondsPerDay)
{
    public bool Allows(DonationTimeDirection direction) =>
        Enabled && (direction == DonationTimeDirection.Add ? AllowAdd : AllowRemove);

    /// <summary>Lee el objeto <c>donationTime</c>; null si no viene (servidor sin esta función).</summary>
    public static DonationTimePolicy? FromJson(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new DonationTimePolicy(
            Bool(el, "enabled", true),
            Bool(el, "allowAdd", true),
            Bool(el, "allowRemove", true),
            Long(el, "maxSecondsPerDonation"),
            Long(el, "maxAddedSecondsPerDay"),
            Long(el, "maxRemovedSecondsPerDay"));
    }

    private static bool Bool(JsonElement el, string name, bool fallback) =>
        el.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean()
            : fallback;

    private static long Long(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.TryGetInt64(out var n) ? n : 0;
}

/// <summary>Convierte diamantes o bits en segundos con la tarifa del streamer.</summary>
public static class DonationTimeCalculator
{
    /// <summary>
    /// Segundos (sin signo) que vale una donación de <paramref name="amount"/> unidades. Las fracciones
    /// no se pierden: <paramref name="carry"/> guarda el sobrante para la siguiente donación
    /// ("cada 10 diamantes = 1 s": diez Rosas de 1 diamante suman 1 s). 0 si no llega al mínimo.
    /// </summary>
    public static long Seconds(DonationTimeRule rule, long amount, ref long carry)
    {
        if (!rule.Enabled || rule.Units <= 0 || rule.Seconds <= 0 || amount <= 0 || amount < Math.Max(1, rule.MinUnits))
        {
            return 0;
        }

        // Aritmética exacta en "unidades × segundos": el resto siempre es menor que Units.
        var numerator = checked(amount * rule.Seconds) + Math.Clamp(carry, 0, rule.Units - 1);
        var seconds = numerator / rule.Units;
        carry = numerator % rule.Units;
        if (rule.MaxSecondsPerDonation > 0)
        {
            seconds = Math.Min(seconds, rule.MaxSecondsPerDonation);
        }

        return seconds;
    }

    /// <summary>Sin sobrante previo ni guardado: para mostrar ejemplos en pantalla.</summary>
    public static long Preview(DonationTimeRule rule, long amount)
    {
        long carry = 0;
        return Seconds(rule, amount, ref carry);
    }

    /// <summary>"45 s", "2 min 30 s", "1 h 5 min".</summary>
    public static string Format(long seconds)
    {
        var s = Math.Abs(seconds);
        if (s < 60)
        {
            return $"{s} s";
        }

        if (s < 3600)
        {
            return s % 60 == 0 ? $"{s / 60} min" : $"{s / 60} min {s % 60} s";
        }

        var m = s % 3600 / 60;
        return m == 0 ? $"{s / 3600} h" : $"{s / 3600} h {m} min";
    }
}
