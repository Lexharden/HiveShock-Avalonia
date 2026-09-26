using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using HiveShock.Live;

namespace HiveShock.Voice;

/// <summary>Resultado de <see cref="TtsMessageFilter.Evaluate"/>: la frase a leer, o por qué se descartó.</summary>
public readonly record struct TtsFilterResult(string? SpokenText, string? SkipReason)
{
    public bool Accepted => SpokenText != null;

    public static TtsFilterResult Skip(string reason) => new(null, reason);
}

/// <summary>
/// Decide qué se lee y cómo suena: limpia el texto (enlaces, emojis, "aaaaaa"), descarta
/// comandos, bots, groserías y spam, aplica cooldown por usuario y arma la frase final
/// ("Juan dice: hola"). Sin estado de UI ni I/O, así se puede probar suelto. Los motivos
/// de descarte están en lenguaje para el streamer (la página Voz muestra el último).
/// </summary>
public sealed partial class TtsMessageFilter
{
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(30);
    private const int RecentTextsToRemember = 30;

    private readonly object _gate = new();
    private readonly Dictionary<string, DateTime> _lastAcceptedByUser = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<(string Text, DateTime At)> _recentTexts = new();

    public TtsFilterResult Evaluate(TtsMessage message, TtsSettings settings, DateTime nowUtc)
    {
        if (!IsPlatformEnabled(message.PortId, settings))
        {
            return TtsFilterResult.Skip($"no se lee {PlatformName(message.PortId)}");
        }

        var speaker = CleanName(message.Speaker);
        if (IsIgnoredUser(message, settings))
        {
            return TtsFilterResult.Skip($"{Who(speaker)} está en la lista de ignorados");
        }

        return message.Kind switch
        {
            TtsMessageKind.Gift => EvaluateGift(message, settings, speaker),
            TtsMessageKind.Follow => settings.ReadFollows
                ? Finish(settings, $"{Who(speaker)} te empezó a seguir")
                : TtsFilterResult.Skip("los follows están desactivados"),
            TtsMessageKind.Bits => settings.ReadBits
                ? Finish(settings, $"{Who(speaker)} envió {message.Count} bits")
                : TtsFilterResult.Skip("los bits están desactivados"),
            _ => EvaluateChat(message, settings, speaker, nowUtc),
        };
    }

    /// <summary>Olvida cooldowns y duplicados (al vaciar la cola o reiniciar Smart TTS).</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _lastAcceptedByUser.Clear();
            _recentTexts.Clear();
        }
    }

    private TtsFilterResult EvaluateChat(
        TtsMessage message,
        TtsSettings settings,
        string speaker,
        DateTime nowUtc)
    {
        if (settings.IgnoreOwnMessages && message.Roles.HasFlag(ChatterRoles.Broadcaster))
        {
            return TtsFilterResult.Skip("es un mensaje tuyo");
        }

        if (!PassesAudience(message.Roles, settings.Audience))
        {
            return TtsFilterResult.Skip(settings.Audience == TtsAudience.ModsOnly
                ? $"{Who(speaker)} no es moderador"
                : $"{Who(speaker)} no es suscriptor ni moderador");
        }

        var text = (message.Text ?? "").Trim();
        if (text.Length == 0)
        {
            return TtsFilterResult.Skip("mensaje vacío");
        }

        if (settings.SkipCommands && (text.StartsWith('!') || text.StartsWith('/')))
        {
            return TtsFilterResult.Skip("es un comando");
        }

        if (settings.SkipMentions && MentionRegex().IsMatch(text))
        {
            return TtsFilterResult.Skip("etiqueta a alguien con @");
        }

        if (settings.RemoveLinks)
        {
            text = LinkRegex().Replace(text, " ");
        }

        if (!settings.ReadEmojis)
        {
            text = RemoveEmojis(text);
        }

        text = RepeatedCharsRegex().Replace(text, "$1$1$1");
        text = RepeatedWordsRegex().Replace(text, "$1 $1");
        text = SpacesRegex().Replace(text, " ").Trim();

        if (!text.Any(char.IsLetterOrDigit))
        {
            return TtsFilterResult.Skip(settings.RemoveLinks && LinkRegex().IsMatch(message.Text ?? "")
                ? "solo tenía un enlace"
                : "solo tenía emojis o símbolos");
        }

        text = Truncate(text, settings.MaxMessageLength);

        var key = string.IsNullOrWhiteSpace(message.SpeakerKey) ? speaker : message.SpeakerKey;
        var normalized = Normalize(text);
        lock (_gate)
        {
            if (settings.PerUserCooldownSeconds > 0 &&
                _lastAcceptedByUser.TryGetValue($"{message.PortId}:{key}", out var last) &&
                nowUtc - last < TimeSpan.FromSeconds(settings.PerUserCooldownSeconds))
            {
                return TtsFilterResult.Skip($"{Who(speaker)} escribió muy seguido");
            }

            while (_recentTexts.Count > 0 && nowUtc - _recentTexts.Peek().At > DuplicateWindow)
            {
                _recentTexts.Dequeue();
            }

            if (_recentTexts.Any(r => r.Text == normalized))
            {
                return TtsFilterResult.Skip("mensaje repetido (spam)");
            }
        }

        var body = settings.SayUserName
            ? settings.SayPlatform
                ? $"{Who(speaker)} en {PlatformName(message.PortId)} dice: {text}"
                : $"{Who(speaker)} dice: {text}"
            : text;

        var result = Finish(settings, body);
        if (result.Accepted)
        {
            lock (_gate)
            {
                _lastAcceptedByUser[$"{message.PortId}:{key}"] = nowUtc;
                _recentTexts.Enqueue((normalized, nowUtc));
                while (_recentTexts.Count > RecentTextsToRemember)
                {
                    _recentTexts.Dequeue();
                }
            }
        }

        return result;
    }

    private static TtsFilterResult EvaluateGift(TtsMessage message, TtsSettings settings, string speaker)
    {
        if (!settings.ReadGifts)
        {
            return TtsFilterResult.Skip("los regalos están desactivados");
        }

        if (message.Diamonds > 0 && message.Diamonds < settings.GiftMinDiamonds)
        {
            return TtsFilterResult.Skip($"regalo de menos de {settings.GiftMinDiamonds} diamantes");
        }

        var gift = string.IsNullOrWhiteSpace(message.Text) ? "un regalo" : message.Text.Trim();
        var sentence = message.Count > 1
            ? $"{Who(speaker)} envió {message.Count} {gift}"
            : $"{Who(speaker)} envió {gift}";
        return Finish(settings, sentence);
    }

    /// <summary>Último paso común: groserías en cualquier parte de la frase (también en el nombre).</summary>
    private static TtsFilterResult Finish(TtsSettings settings, string sentence)
    {
        if (ContainsBlockedWord(sentence, settings.BlockedWords))
        {
            return TtsFilterResult.Skip("contiene una palabra bloqueada");
        }

        return new TtsFilterResult(sentence, null);
    }

    private static bool IsPlatformEnabled(string portId, TtsSettings settings)
    {
        if (string.Equals(portId, LivePortIds.TikTok, StringComparison.OrdinalIgnoreCase))
        {
            return settings.ReadTikTok;
        }

        if (string.Equals(portId, LivePortIds.Twitch, StringComparison.OrdinalIgnoreCase))
        {
            return settings.ReadTwitch;
        }

        return true;
    }

    private static bool IsIgnoredUser(TtsMessage message, TtsSettings settings)
    {
        if (settings.IgnoredUsers.Count == 0)
        {
            return false;
        }

        foreach (var raw in settings.IgnoredUsers)
        {
            var ignored = raw.Trim().TrimStart('@');
            if (ignored.Length == 0)
            {
                continue;
            }

            if (string.Equals(ignored, message.Speaker?.Trim(), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ignored, message.SpeakerKey?.Trim().TrimStart('@'), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool PassesAudience(ChatterRoles roles, TtsAudience audience) => audience switch
    {
        TtsAudience.ModsOnly => (roles & (ChatterRoles.Moderator | ChatterRoles.Broadcaster)) != 0,
        TtsAudience.SubscribersAndMods =>
            (roles & (ChatterRoles.Moderator | ChatterRoles.Broadcaster | ChatterRoles.Subscriber | ChatterRoles.Vip)) != 0,
        _ => true,
    };

    private static bool ContainsBlockedWord(string text, IReadOnlyList<string> blocked)
    {
        if (blocked.Count == 0)
        {
            return false;
        }

        var haystack = $" {Normalize(text)} ";
        foreach (var word in blocked)
        {
            var needle = Normalize(word);
            if (needle.Length > 0 && haystack.Contains($" {needle} ", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Minúsculas, sin acentos, sin signos y con espacios simples: "¡HOLA, Güey!" → "hola guey".</summary>
    private static string Normalize(string text)
    {
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }

        return SpacesRegex().Replace(sb.ToString(), " ").Trim();
    }

    private static string RemoveEmojis(string text)
    {
        var sb = new StringBuilder(text.Length);
        var e = StringInfo.GetTextElementEnumerator(text);
        while (e.MoveNext())
        {
            var element = e.GetTextElement();
            var first = char.ConvertToUtf32(element, 0);
            var category = CharUnicodeInfo.GetUnicodeCategory(first);
            var isEmoji = category is UnicodeCategory.OtherSymbol or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse
                          || first is >= 0x1F000 and <= 0x1FAFF
                          || first is >= 0x2600 and <= 0x27BF;
            sb.Append(isEmoji ? " " : element);
        }

        return sb.ToString();
    }

    /// <summary>Nombre pronunciable: sin emojis, guiones bajos como espacios.</summary>
    private static string CleanName(string? name)
    {
        var cleaned = RemoveEmojis(name ?? "").Replace('_', ' ').Replace('.', ' ');
        return SpacesRegex().Replace(cleaned, " ").Trim();
    }

    private static string Who(string speaker) => string.IsNullOrWhiteSpace(speaker) ? "Alguien" : speaker;

    private static string PlatformName(string portId) =>
        string.Equals(portId, LivePortIds.Twitch, StringComparison.OrdinalIgnoreCase) ? "Twitch" :
        string.Equals(portId, LivePortIds.TikTok, StringComparison.OrdinalIgnoreCase) ? "TikTok" : portId;

    private static string Truncate(string text, int max)
    {
        if (text.Length <= max)
        {
            return text;
        }

        var cut = text[..max];
        var lastSpace = cut.LastIndexOf(' ');
        return (lastSpace > max / 2 ? cut[..lastSpace] : cut).TrimEnd(',', ';', ':', ' ');
    }

    [GeneratedRegex(@"(https?://\S+|www\.\S+|\b[\w-]+\.(com|net|org|tv|gg|ly|io|me|co|xyz|link|live)(/\S*)?\b)", RegexOptions.IgnoreCase)]
    private static partial Regex LinkRegex();

    /// <summary>"@usuario" suelto (no "ana@gmail.com": la @ no puede ir pegada a una letra o número).</summary>
    [GeneratedRegex(@"(?<![\p{L}\p{N}])@[\p{L}\p{N}_.]+")]
    private static partial Regex MentionRegex();

    [GeneratedRegex(@"(\p{L})\1{3,}", RegexOptions.IgnoreCase)]
    private static partial Regex RepeatedCharsRegex();

    [GeneratedRegex(@"\b(\w+)(\s+\1\b){2,}", RegexOptions.IgnoreCase)]
    private static partial Regex RepeatedWordsRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacesRegex();
}
