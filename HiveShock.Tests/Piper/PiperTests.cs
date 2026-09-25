using System.Text.Json;
using HiveShock.Voice;
using HiveShock.Voice.Piper;
using HiveShock.Voice.Piper.Runtime;
using HiveShock.Voice.Piper.Synthesis;
using HiveShock.Voice.Piper.Voices;

namespace HiveShock.Tests.Piper;

/// <summary>Prueba real (descarga ~40 MB): solo corre con HIVESHOCK_PIPER_IT=1.</summary>
public sealed class PiperIntegrationFactAttribute : FactAttribute
{
    public PiperIntegrationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("HIVESHOCK_PIPER_IT") != "1")
        {
            Skip = "Integración con Piper real: define HIVESHOCK_PIPER_IT=1 para ejecutarla.";
        }
    }
}

public sealed class PiperTests : IDisposable
{
    private const string CatalogJson = """
    {
      "es_MX-claude-high": {
        "key": "es_MX-claude-high", "name": "claude",
        "language": { "code": "es_MX", "family": "es" }, "quality": "high", "num_speakers": 1, "speaker_id_map": {},
        "files": {
          "es/es_MX/claude/high/es_MX-claude-high.onnx": { "size_bytes": 63000000, "md5_digest": "aa" },
          "es/es_MX/claude/high/es_MX-claude-high.onnx.json": { "size_bytes": 5000, "md5_digest": "bb" },
          "es/es_MX/claude/high/MODEL_CARD": { "size_bytes": 300, "md5_digest": "cc" }
        }
      },
      "es_ES-sharvard-medium": {
        "key": "es_ES-sharvard-medium", "name": "sharvard",
        "language": { "code": "es_ES", "family": "es" }, "quality": "medium", "num_speakers": 2,
        "speaker_id_map": { "M": 0, "F": 1 },
        "files": {
          "es/es_ES/sharvard/medium/es_ES-sharvard-medium.onnx": { "size_bytes": 76000000, "md5_digest": "dd" },
          "es/es_ES/sharvard/medium/es_ES-sharvard-medium.onnx.json": { "size_bytes": 5000, "md5_digest": "ee" }
        }
      },
      "pt_PT-tugão-medium": {
        "key": "pt_PT-tugão-medium", "name": "tugão",
        "language": { "code": "pt_PT", "family": "pt" }, "quality": "medium", "num_speakers": 1,
        "files": {
          "pt/pt_PT/tugão/medium/pt_PT-tugão-medium.onnx": { "size_bytes": 1, "md5_digest": "ff" },
          "pt/pt_PT/tugão/medium/pt_PT-tugão-medium.onnx.json": { "size_bytes": 1, "md5_digest": "gg" }
        }
      },
      "roto": { "key": "../../fuera", "name": "x", "language": { "code": "es_ES" }, "quality": "low",
                "files": { "a.onnx": { "size_bytes": 1, "md5_digest": "x" }, "a.onnx.json": { "size_bytes": 1, "md5_digest": "y" } } },
      "sin-modelo": { "key": "sin-modelo", "name": "y", "language": { "code": "es_ES" }, "quality": "low", "files": {} }
    }
    """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"hiveshock-piper-test-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Catalog_parses_voices_and_skips_unsafe_or_incomplete_entries()
    {
        var voices = PiperVoiceCatalog.Parse(CatalogJson);

        Assert.Equal(["es_MX-claude-high", "es_ES-sharvard-medium", "pt_PT-tugão-medium"], voices.Select(v => v.Key));
        var claude = voices[0];
        Assert.Equal("es-MX", claude.Locale);
        Assert.Equal("Claude", claude.DisplayName);
        Assert.Equal("calidad alta", claude.QualityLabel);
        Assert.Equal(63005300, claude.SizeBytes);
        Assert.EndsWith("es/es_MX/claude/high/MODEL_CARD", claude.LicenseUrl);
        Assert.Equal(2, voices[1].Speakers.Count);
    }

    [Theory]
    [InlineData("es_MX-claude-high", true)]
    [InlineData("pt_PT-tugão-medium", true)]
    [InlineData("../../fuera", false)]
    [InlineData("a/b", false)]
    [InlineData("C:\\x", false)]
    [InlineData("", false)]
    public void Only_plain_keys_can_become_folders(string key, bool safe)
    {
        Assert.Equal(safe, PiperVoiceStore.IsSafeKey(key));
    }

    [Theory]
    [InlineData(0, 1.0)]
    [InlineData(100, 0.5)]
    [InlineData(-50, 2.0)]
    [InlineData(50, 0.65)]
    [InlineData(500, 0.5)]
    [InlineData(-99, 2.0)]
    public void Rate_maps_to_rounded_length_scale(int rate, double scale)
    {
        Assert.Equal(scale, PiperSpeechSynthesizer.LengthScaleFor(rate), 3);
    }

    [Fact]
    public async Task Installed_voices_become_voice_profiles_including_each_speaker()
    {
        var (_, store, synth) = CreateWithInstalledVoices();

        var voices = await synth.ListVoicesAsync(CancellationToken.None);

        Assert.Equal(["es_ES-sharvard-medium#0", "es_ES-sharvard-medium#1", "es_MX-claude-high"], voices.Select(v => v.Id));
        Assert.Equal("Sharvard F (calidad media)", voices[1].DisplayName);
        Assert.Equal("Claude (calidad alta)", voices[2].DisplayName);
        Assert.All(voices, v => Assert.StartsWith("es-", v.Locale));
        Assert.Equal(2, store.Installed().Count);
    }

    [Fact]
    public void Voice_ids_resolve_speakers_and_fall_back_to_an_installed_voice()
    {
        var (_, _, synth) = CreateWithInstalledVoices();

        var multi = synth.Resolve("es_ES-sharvard-medium#1")!.Value;
        Assert.Equal("es_ES-sharvard-medium", multi.Voice.Info.Key);
        Assert.Equal(1, multi.SpeakerId);

        Assert.Equal(0, synth.Resolve("es_ES-sharvard-medium")!.Value.SpeakerId);
        Assert.Null(synth.Resolve("es_MX-claude-high#5")!.Value.SpeakerId); // un solo hablante: se ignora
        Assert.StartsWith("es_", synth.Resolve("no-existe")!.Value.Voice.Info.Key);
    }

    [Fact]
    public async Task Engine_is_unavailable_until_runtime_and_a_voice_are_installed()
    {
        using var engine = new PiperTtsEngine(new PiperPaths(_root));
        Assert.False(engine.IsAvailable);
        Assert.False(engine.SupportsPitch);
        Assert.False(engine.RequiresInternet);
        Assert.Contains("instalar", engine.UnavailableReason);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.Synthesizer.SynthesizeAsync("hola", new VoiceProfile("", "", ""), SpeechStyle.Normal, CancellationToken.None));
        Assert.Contains("Piper", error.Message);
    }

    [Fact]
    public void Worker_pool_keeps_a_limit_and_evicts_the_least_used()
    {
        using var pool = new PiperWorkerPool(maxWorkers: 2);
        PiperWorkerOptions Options(string model, double scale = 1) => new("piper", "espeak", model, model + ".json", scale, _root);

        var a = pool.Get(Options("a"));
        Thread.Sleep(5);
        var b = pool.Get(Options("b"));
        Assert.Same(a, pool.Get(Options("a")));
        Assert.NotSame(a, pool.Get(Options("a", 0.5))); // otra velocidad = otro proceso
        Assert.Equal(2, pool.Count);
        Assert.NotSame(b, pool.Get(Options("b"))); // b era el menos usado y se cerró

        pool.Clear();
        Assert.Equal(0, pool.Count);
    }

    [Fact]
    public async Task Custom_voices_are_found_loose_or_in_subfolders_and_read_their_language()
    {
        var paths = new PiperPaths(_root);
        Directory.CreateDirectory(Path.Combine(paths.VoicesDirectory, "Mis voces"));
        WriteCustom(Path.Combine(paths.VoicesDirectory, "Mi Voz"), """{ "language": { "code": "es_AR" }, "audio": { "quality": "high" }, "num_speakers": 1 }""");
        WriteCustom(Path.Combine(paths.VoicesDirectory, "Mis voces", "narrador#2"), """{ "espeak": { "voice": "en-us" }, "num_speakers": 2, "speaker_id_map": { "a": 0, "b": 1 } }""");
        File.WriteAllText(Path.Combine(paths.VoicesDirectory, "sin-config.onnx"), ""); // falta su .onnx.json: se ignora

        var store = new PiperVoiceStore(paths);
        var voices = store.Installed();

        Assert.Equal(["Mi Voz", "narrador-2"], voices.Select(v => v.Info.Key)); // '#' no puede ir en la clave
        Assert.All(voices, v => Assert.True(v.IsCustom));
        Assert.Equal("es-AR", voices[0].Info.Locale);
        Assert.Equal("en-us", voices[1].Info.Locale);

        var synth = new PiperSpeechSynthesizer(new PiperRuntime(paths), store, paths);
        var profiles = await synth.ListVoicesAsync(CancellationToken.None);
        Assert.Contains(profiles, p => p.Id == "Mi Voz" && p.DisplayName == "Mi Voz (voz propia)");
        Assert.Contains(profiles, p => p.Id == "narrador-2#1");

        store.Delete("narrador-2");
        Assert.Equal(["Mi Voz"], store.Installed().Select(v => v.Info.Key));
        Assert.True(Directory.Exists(Path.Combine(paths.VoicesDirectory, "Mis voces"))); // la subcarpeta del streamer se respeta
    }

    [Fact]
    public void Voices_copied_later_appear_after_refresh()
    {
        var paths = new PiperPaths(_root);
        var store = new PiperVoiceStore(paths);
        Assert.Empty(store.Installed());

        Directory.CreateDirectory(paths.VoicesDirectory);
        WriteCustom(Path.Combine(paths.VoicesDirectory, "nueva"), """{ "language": { "code": "es_ES" } }""");
        Assert.Empty(store.Installed()); // en caché hasta "Buscar voces nuevas"

        store.Refresh();
        Assert.Single(store.Installed());
    }

    [Theory]
    [InlineData(unchecked((int)0xC0000135), true)]
    [InlineData(unchecked((int)0xC000007B), true)]
    [InlineData(unchecked((int)0xC0000142), true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public void Missing_library_exit_codes_are_recognized(int exitCode, bool missing)
    {
        Assert.Equal(missing, PiperDependencyException.IsMissingLibraryExitCode(exitCode));
    }

    private static void WriteCustom(string pathWithoutExtension, string configJson)
    {
        File.WriteAllText(pathWithoutExtension + ".onnx", "modelo");
        File.WriteAllText(pathWithoutExtension + ".onnx.json", configJson);
    }

    [Fact]
    public void There_is_a_pinned_binary_for_this_test_machine()
    {
        var asset = PiperRuntime.CurrentAsset;
        Assert.NotNull(asset);
        Assert.Equal(64, asset!.Sha256.Length);
        Assert.False(new PiperRuntime(new PiperPaths(_root)).IsInstalled);
    }

    [PiperIntegrationFact]
    public async Task Real_install_download_and_synthesis()
    {
        using var engine = new PiperTtsEngine(new PiperPaths(_root));
        await engine.Runtime.InstallAsync(null, CancellationToken.None);
        var catalog = await engine.Catalog.LoadAsync(false, CancellationToken.None);
        await engine.Store.InstallAsync(catalog.First(v => v.Key == "es_MX-ald-x_low"), null, CancellationToken.None);

        Assert.True(engine.IsAvailable);
        await engine.CheckAsync("", CancellationToken.None);
        await using var audio = await engine.Synthesizer.SynthesizeAsync(
            "¿Qué tal, niño?", new VoiceProfile("es_MX-ald-x_low", "", ""), new SpeechStyle(30, 0), CancellationToken.None);
        Assert.True(audio.Length > 10_000);
    }

    /// <summary>Simula dos voces descargadas (archivos vacíos; no se sintetiza con ellas).</summary>
    private (PiperPaths Paths, PiperVoiceStore Store, PiperSpeechSynthesizer Synth) CreateWithInstalledVoices()
    {
        var paths = new PiperPaths(_root);
        foreach (var voice in PiperVoiceCatalog.Parse(CatalogJson).Where(v => v.LanguageCode.StartsWith("es")))
        {
            var dir = Path.Combine(paths.VoicesDirectory, voice.Key);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, Path.GetFileName(voice.Model!.Path)), "");
            File.WriteAllText(Path.Combine(dir, Path.GetFileName(voice.Config!.Path)), "{}");
            File.WriteAllText(Path.Combine(dir, "voice.json"), JsonSerializer.Serialize(voice));
        }

        var store = new PiperVoiceStore(paths);
        return (paths, store, new PiperSpeechSynthesizer(new PiperRuntime(paths), store, paths));
    }
}
