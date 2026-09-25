using System.Collections.Concurrent;
using System.Globalization;

namespace HiveShock.Voice;

/// <summary>
/// Nombres de idioma y país en español para agrupar voces ("es-MX" → idioma "Español",
/// país "México"), sin depender del idioma de Windows del streamer. Usa los datos de
/// cultura del sistema (ICU), no una tabla propia, así cubre todos los idiomas de Edge.
/// </summary>
public static class VoiceLanguages
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es");
    private static readonly ConcurrentDictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>"es-MX" → "es"; "fil-PH" → "fil".</summary>
    public static string LanguageCode(string? locale)
    {
        var value = (locale ?? "").Trim();
        var dash = value.IndexOf('-');
        return (dash > 0 ? value[..dash] : value).ToLowerInvariant();
    }

    /// <summary>"es" → "Español"; "ja" → "Japonés". Si no se conoce, el código en mayúsculas.</summary>
    public static string LanguageName(string code) =>
        Names.GetOrAdd("lang:" + code, _ => Capitalize(SpanishDisplayName(code) ?? code.ToUpperInvariant()));

    /// <summary>"es-MX" → "México"; "en-GB" → "Reino Unido"; sin país → "".</summary>
    public static string RegionName(string? locale) =>
        Names.GetOrAdd("region:" + locale, _ =>
        {
            var display = SpanishDisplayName(locale ?? "");
            if (display == null)
            {
                return "";
            }

            var open = display.IndexOf('(');
            var close = display.LastIndexOf(')');
            return open >= 0 && close > open ? display[(open + 1)..close].Trim() : "";
        });

    /// <summary>Orden de grupos: español primero, luego alfabético por nombre en español.</summary>
    public static int SortKey(string code) => code == "es" ? 0 : 1;

    private static string? SpanishDisplayName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        // DisplayName se genera en el idioma de la UI del hilo: se fija a español solo aquí.
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = Spanish;
            var culture = new CultureInfo(name);
            return culture.DisplayName is { Length: > 0 } display && !display.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase)
                ? display
                : null;
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    private static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpper(text[0], Spanish) + text[1..];
}
