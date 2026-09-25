using System.Text.Json;
using HiveShock.Configuration;
using HiveShock.Voice;

namespace HiveShock.Tests.Voice;

public class TtsSettingsTests
{
    [Fact]
    public void Old_files_migrate_voices_to_per_engine_dictionary()
    {
        const string oldJson = """{ "Engine": "windows", "VoiceId": "es-MX-DaliaNeural", "WindowsVoiceId": "sabina" }""";

        var settings = JsonSerializer.Deserialize<TtsSettings>(oldJson, UserDataStore.JsonOptions)!;
        settings.Normalize();

        Assert.Equal("es-MX-DaliaNeural", settings.GetVoiceId(TtsEngines.Edge));
        Assert.Equal("sabina", settings.GetVoiceId(TtsEngines.Windows));
        Assert.Equal(TtsEngines.Windows, settings.Engine);
    }

    [Fact]
    public void Saving_writes_only_the_new_format()
    {
        var settings = new TtsSettings();
        settings.SetVoiceId(TtsEngines.Edge, "es-MX-DaliaNeural");

        var json = JsonSerializer.Serialize(settings, UserDataStore.JsonOptions);

        Assert.Contains("\"VoiceIds\"", json);
        Assert.DoesNotContain("\"VoiceId\"", json);
        Assert.DoesNotContain("\"WindowsVoiceId\"", json);
        var back = JsonSerializer.Deserialize<TtsSettings>(json, UserDataStore.JsonOptions)!;
        Assert.Equal("es-MX-DaliaNeural", back.GetVoiceId("EDGE")); // sin distinguir mayúsculas
    }

    [Fact]
    public void Unknown_engine_is_kept_so_a_newer_config_is_not_lost()
    {
        var settings = new TtsSettings { Engine = " Piper " };
        settings.Normalize();
        Assert.Equal(TtsEngines.Piper, settings.Engine);
    }

    [Fact]
    public void Out_of_range_values_are_clamped()
    {
        var settings = new TtsSettings { Volume = 3, RatePercent = 500, MicSensitivity = -4, MaxMessageLength = 1 };
        settings.Normalize();
        Assert.Equal(1, settings.Volume);
        Assert.Equal(100, settings.RatePercent);
        Assert.Equal(0, settings.MicSensitivity);
        Assert.Equal(20, settings.MaxMessageLength);
    }
}

public class TtsEngineRegistryTests
{
    private readonly FakeEngine _edge = new(TtsEngines.Edge, requiresInternet: true);
    private readonly FakeEngine _windows = new(TtsEngines.Windows, requiresInternet: false);
    private readonly FakeEngine _piper = new(TtsEngines.Piper, requiresInternet: false, available: false);

    [Fact]
    public void First_available_engine_is_the_default()
    {
        var registry = new TtsEngineRegistry([_piper, _edge, _windows]);
        Assert.Same(_edge, registry.Default);
    }

    [Fact]
    public void Resolve_falls_back_for_unknown_or_unavailable_engines()
    {
        var registry = new TtsEngineRegistry([_edge, _windows, _piper]);
        Assert.Same(_windows, registry.Resolve("WINDOWS"));
        Assert.Same(_edge, registry.Resolve("piper"));
        Assert.Same(_edge, registry.Resolve("no-existe"));
        Assert.Same(_edge, registry.Resolve(null));
    }

    [Fact]
    public void Offline_fallback_is_the_first_available_engine_without_internet()
    {
        Assert.Same(_windows, new TtsEngineRegistry([_edge, _piper, _windows]).OfflineFallback);
        Assert.Null(new TtsEngineRegistry([_edge, _piper]).OfflineFallback);
    }

    [Fact]
    public void Available_hides_engines_that_are_not_installed()
    {
        var registry = new TtsEngineRegistry([_edge, _windows, _piper]);
        Assert.Equal([TtsEngines.Edge, TtsEngines.Windows], registry.Available.Select(e => e.Id));
    }

    [Fact]
    public void Duplicates_and_empty_lists_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new TtsEngineRegistry([_edge, new FakeEngine("EDGE", true)]));
        Assert.Throws<ArgumentException>(() => new TtsEngineRegistry([]));
    }
}

public class VoiceLanguagesTests
{
    [Theory]
    [InlineData("es-MX", "es")]
    [InlineData("fil-PH", "fil")]
    [InlineData("EN", "en")]
    [InlineData("", "")]
    public void Language_code_is_the_part_before_the_dash(string locale, string code)
    {
        Assert.Equal(code, VoiceLanguages.LanguageCode(locale));
    }

    [Fact]
    public void Names_are_in_spanish_regardless_of_system_language()
    {
        Assert.Equal("Español", VoiceLanguages.LanguageName("es"));
        Assert.Equal("Japonés", VoiceLanguages.LanguageName("ja"));
        Assert.Equal("Reino Unido", VoiceLanguages.RegionName("en-GB"));
        Assert.Equal("", VoiceLanguages.RegionName("es"));
    }
}
