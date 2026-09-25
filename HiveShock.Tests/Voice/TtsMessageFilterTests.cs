using HiveShock.Voice;

namespace HiveShock.Tests.Voice;

public class TtsMessageFilterTests
{
    private static readonly DateTime Now = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

    private readonly TtsMessageFilter _filter = new();
    private readonly TtsSettings _settings = new() { PerUserCooldownSeconds = 8, BlockedWords = ["tonto"] };

    private TtsFilterResult Eval(string text, string user = "juan_123", ChatterRoles roles = ChatterRoles.None,
        string port = "tiktok", DateTime? at = null) =>
        _filter.Evaluate(new TtsMessage(port, user, text, Now) { Roles = roles, SpeakerKey = user }, _settings, at ?? Now);

    [Fact]
    public void Normal_message_is_read_with_clean_speaker_name()
    {
        Assert.Equal("juan 123 dice: hola a todos", Eval("hola a todos").SpokenText);
    }

    [Fact]
    public void Can_say_platform_or_omit_name()
    {
        _settings.SayPlatform = true;
        Assert.Equal("ana en Twitch dice: hola", Eval("hola", "ana", port: "twitch").SpokenText);

        _settings.SayUserName = false;
        Assert.Equal("buenas", Eval("buenas", "leo").SpokenText);
    }

    [Fact]
    public void Same_user_waits_for_cooldown()
    {
        Assert.True(Eval("primero").Accepted);
        Assert.Equal("juan 123 escribió muy seguido", Eval("segundo").SkipReason);
        Assert.True(Eval("tercero", at: Now.AddSeconds(9)).Accepted);
    }

    [Theory]
    [InlineData("!join", "es un comando")]
    [InlineData("/me baila", "es un comando")]
    [InlineData("www.spam.com", "solo tenía un enlace")]
    [InlineData("😂😂😂", "solo tenía emojis o símbolos")]
    [InlineData("   ", "mensaje vacío")]
    public void Junk_is_skipped_with_a_reason(string text, string reason)
    {
        Assert.Equal(reason, Eval(text, Guid.NewGuid().ToString("N")).SkipReason);
    }

    [Fact]
    public void Links_are_removed_and_repeated_letters_shortened()
    {
        Assert.Equal("luis dice: mira ya", Eval("mira https://spam.com/x ya", "luis").SpokenText);
        Assert.Equal("rosa dice: jajajaja holaaa", Eval("jajajaja holaaaaaaaa", "rosa").SpokenText);
    }

    [Fact]
    public void Blocked_words_ignore_case_and_accents_in_text_and_names()
    {
        Assert.Equal("contiene una palabra bloqueada", Eval("eres un TÓNTO", "troll").SkipReason);
        Assert.False(Eval("hola", "el_tonto").Accepted);
        Assert.True(Eval("montón de cosas", "otro").Accepted);
    }

    [Fact]
    public void Bots_own_messages_and_copied_spam_are_skipped()
    {
        Assert.False(Eval("hola", "Nightbot", port: "twitch").Accepted);
        Assert.Equal("es un mensaje tuyo", Eval("hola", "yo", ChatterRoles.Broadcaster).SkipReason);
        Assert.True(Eval("hola a todos", "a1").Accepted);
        Assert.Equal("mensaje repetido (spam)", Eval("¡Hola a todos!", "a2").SkipReason);
    }

    [Fact]
    public void Audience_filter_respects_roles()
    {
        _settings.Audience = TtsAudience.SubscribersAndMods;
        Assert.False(Eval("buenas", "x1").Accepted);
        Assert.True(Eval("buenas tardes", "x2", ChatterRoles.Subscriber).Accepted);

        _settings.Audience = TtsAudience.ModsOnly;
        Assert.False(Eval("hey", "x3", ChatterRoles.Subscriber | ChatterRoles.Vip).Accepted);
        Assert.True(Eval("hey hey", "x4", ChatterRoles.Moderator).Accepted);
    }

    [Fact]
    public void Platform_can_be_muted()
    {
        _settings.ReadTwitch = false;
        Assert.Equal("no se lee Twitch", Eval("hey", "tw", port: "twitch").SkipReason);
        Assert.True(Eval("hey", "tk", port: "tiktok").Accepted);
    }

    [Fact]
    public void Long_messages_are_cut_at_a_word()
    {
        _settings.MaxMessageLength = 30;
        var spoken = Eval("este es un mensaje bastante largo que deberia cortarse", "largo").SpokenText!;
        Assert.Equal("largo dice: este es un mensaje bastante", spoken);
    }

    [Fact]
    public void Gifts_bits_and_follows_build_sentences()
    {
        TtsFilterResult Event(TtsMessageKind kind, string text = "", int count = 0, long diamonds = 0) =>
            _filter.Evaluate(new TtsMessage("tiktok", "Ana", text, Now) { Kind = kind, Count = count, Diamonds = diamonds }, _settings, Now);

        Assert.Equal("Ana envió 5 Rosa", Event(TtsMessageKind.Gift, "Rosa", 5, 5).SpokenText);
        Assert.Equal("Ana envió 100 bits", Event(TtsMessageKind.Bits, count: 100).SpokenText);
        Assert.False(Event(TtsMessageKind.Follow).Accepted); // apagado por defecto

        _settings.GiftMinDiamonds = 10;
        Assert.False(Event(TtsMessageKind.Gift, "Rosa", 1, 1).Accepted);
    }
}
