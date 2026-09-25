using System.Net.Http;
using HiveShock.Voice;

namespace HiveShock.Tests.Voice;

public sealed class SmartVoiceManagerTests : IDisposable
{
    private static readonly VoiceProfile[] Voices =
    [
        new("es-MX-A", "A", "es-MX"), new("es-MX-B", "B", "es-MX"), new("es-ES-C", "C", "es-ES"),
        new("en-US-D", "D", "en-US"), new("en-US-E", "E", "en-US"), new("ja-JP-F", "F", "ja-JP"),
    ];

    private readonly FakeEngine _edge = new(TtsEngines.Edge, requiresInternet: true, new FakeSynthesizer(Voices));
    private readonly FakeEngine _local = new(TtsEngines.Windows, requiresInternet: false, new FakeSynthesizer([new("win-1", "Sabina", "es-MX")]), supportsPitch: false);
    private readonly FakePlayer _player = new();
    private readonly FakeMicrophone _mic = new();
    private readonly FakeHotkeys _hotkeys = new();
    private readonly FakeVad _vad = new();
    private readonly TtsSettings _settings = new() { Enabled = true, PerUserCooldownSeconds = 0, PauseWhenISpeak = false };
    private readonly SmartVoiceManager _manager;

    public SmartVoiceManagerTests()
    {
        _settings.SetVoiceId(TtsEngines.Edge, "es-MX-A");
        _settings.SetVoiceId(TtsEngines.Windows, "win-1");
        _manager = new SmartVoiceManager(_mic, new TtsEngineRegistry([_edge, _local]), _player, _hotkeys, _vad, _settings);
    }

    public void Dispose() => _manager.Dispose();

    private void Say(string user, string text, DateTime? at = null) =>
        _manager.Enqueue(new TtsMessage("tiktok", user, text, at ?? DateTime.UtcNow) { SpeakerKey = user });

    private static async Task<bool> WaitFor(Func<bool> condition, int timeoutMs = 3000)
    {
        var end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < end)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(20);
        }

        return condition();
    }

    [Fact]
    public async Task Reads_accepted_messages_and_counts_filtered_ones()
    {
        _manager.Start();
        Say("ana", "primer mensaje");
        Say("bot", "!comando");
        Say("leo", "segundo mensaje");

        Assert.True(await WaitFor(() => _player.PlayedCount == 2));
        Assert.Equal(1, _manager.FilteredCount);
        Assert.Equal("es un comando", _manager.LastFilteredReason);
        Assert.All(_edge.Fake.Calls, c => Assert.Equal("es-MX-A", c.VoiceId));
    }

    [Fact]
    public async Task Nothing_is_read_while_stopped_muted_or_after_clearing()
    {
        Say("ana", "apagado");
        Assert.Empty(_edge.Fake.Calls);

        _manager.Start();
        _manager.IsMuted = true;
        Say("ana", "silenciado");
        await Task.Delay(300);
        Assert.Equal(0, _player.PlayedCount);

        _manager.ClearQueue();
        _manager.IsMuted = false;
        await Task.Delay(300);
        Assert.Equal(0, _player.PlayedCount);
    }

    [Fact]
    public async Task Skip_cuts_the_current_message_and_moves_on()
    {
        _player.PlayDuration = TimeSpan.FromSeconds(5);
        _manager.Start();
        Say("ana", "mensaje largo");
        Say("leo", "siguiente");

        Assert.True(await WaitFor(() => _manager.NowSpeaking.Contains("ana")));
        _hotkeys.PressSkip();
        Assert.True(await WaitFor(() => _manager.NowSpeaking.Contains("leo")));
    }

    [Fact]
    public async Task Streamer_speaking_pauses_reading()
    {
        _settings.PauseWhenISpeak = true;
        _manager.Start();
        Assert.True(_mic.IsCapturing);

        _vad.SetSpeaking(true);
        Say("ana", "espera a que termine");
        await Task.Delay(300);
        Assert.Equal(0, _player.PlayedCount);

        _vad.SetSpeaking(false);
        Assert.True(await WaitFor(() => _player.PlayedCount == 1));
    }

    [Fact]
    public async Task Message_cut_by_the_streamer_is_replayed_whole_when_they_stop()
    {
        _settings.PauseWhenISpeak = true;
        _player.PlayDuration = TimeSpan.FromMilliseconds(800);
        _manager.Start();
        Say("ana", "mensaje que voy a interrumpir");

        Assert.True(await WaitFor(() => _manager.NowSpeaking.Contains("ana")));
        _vad.SetSpeaking(true);
        Assert.True(await WaitFor(() => _manager.NowSpeaking.Length == 0));
        await Task.Delay(300);
        Assert.Equal(0, _player.PlayedCount);

        _vad.SetSpeaking(false);
        Assert.True(await WaitFor(() => _player.PlayedCount == 1, 5000));
        Assert.Equal(2, _player.StartedCount);        // empezó, se cortó y se repitió entero
        Assert.Single(_edge.Fake.Calls);              // sin volver a sintetizar
        Assert.Equal(1, _manager.SpokenCount);
    }

    [Fact]
    public async Task With_replay_off_a_cut_message_is_dropped_and_the_next_one_plays()
    {
        _settings.PauseWhenISpeak = true;
        _settings.ReplayAfterInterruption = false;
        _player.PlayDuration = TimeSpan.FromMilliseconds(800);
        _manager.Start();
        Say("ana", "primero");
        Say("leo", "segundo");

        Assert.True(await WaitFor(() => _manager.NowSpeaking.Contains("ana")));
        _vad.SetSpeaking(true);
        await Task.Delay(200);
        _vad.SetSpeaking(false);

        Assert.True(await WaitFor(() => _manager.NowSpeaking.Contains("leo")));
        Assert.True(await WaitFor(() => _player.PlayedCount == 1, 5000));
        Assert.Equal(2, _player.StartedCount); // ana cortada (sin repetir) + leo
    }

    [Fact]
    public async Task Skipping_never_replays()
    {
        _settings.PauseWhenISpeak = true;
        _player.PlayDuration = TimeSpan.FromMilliseconds(800);
        _manager.Start();
        Say("ana", "me van a saltar");
        Say("leo", "siguiente");

        Assert.True(await WaitFor(() => _manager.NowSpeaking.Contains("ana")));
        _hotkeys.PressSkip();
        Assert.True(await WaitFor(() => _player.PlayedCount == 1, 5000));
        await Task.Delay(1200);
        Assert.Equal(2, _player.StartedCount);
        Assert.Equal(1, _player.PlayedCount);
    }

    [Fact]
    public async Task A_message_interrupted_too_many_times_is_given_up()
    {
        _settings.PauseWhenISpeak = true;
        _player.PlayDuration = TimeSpan.FromSeconds(2);
        _manager.Start();
        Say("ana", "me interrumpen mucho");

        for (var i = 0; i < 3; i++)
        {
            Assert.True(await WaitFor(() => _manager.NowSpeaking.Contains("ana"), 5000));
            _vad.SetSpeaking(true);
            Assert.True(await WaitFor(() => _manager.NowSpeaking.Length == 0));
            _vad.SetSpeaking(false);
        }

        Assert.True(await WaitFor(() => _manager.LastFilteredReason == "se cortó varias veces porque hablaste", 5000));
        Assert.Equal(3, _player.StartedCount); // original + 2 repeticiones
        Assert.Equal(0, _player.PlayedCount);
    }

    [Fact]
    public async Task Old_messages_are_dropped()
    {
        _manager.Start();
        Say("viejo", "de hace un minuto", DateTime.UtcNow.AddMinutes(-1));
        Assert.True(await WaitFor(() => _manager.LastFilteredReason == "llevaba demasiado tiempo esperando"));
        Assert.Equal(0, _player.PlayedCount);
    }

    [Fact]
    public async Task Blocked_provider_locks_reading_until_switching_to_an_offline_engine()
    {
        _settings.UseFallbackEngines = false;
        _edge.Fake.Failure = () => new SpeechServiceBlockedException("403");
        _manager.Start();
        await _manager.CheckServiceAsync(CancellationToken.None);

        Assert.True(_manager.IsBlocked);
        Say("ana", "no debe entrar");
        Assert.Equal(0, _manager.QueueCount);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _manager.PlaySampleAsync("hola", CancellationToken.None));

        _settings.Engine = _manager.Engines.OfflineFallback!.Id;
        Assert.False(_manager.IsBlocked);
        await _manager.CheckServiceAsync(CancellationToken.None);
        Assert.Equal(VoiceServiceState.Ready, _manager.ServiceState);
    }

    [Fact]
    public async Task Offline_engine_falls_back_to_the_next_one_and_keeps_the_warning()
    {
        _edge.Fake.Failure = () => new HttpRequestException("No such host is known.");
        _manager.Start();
        Say("ana", "sin internet");

        Assert.True(await WaitFor(() => _player.PlayedCount == 1));
        Assert.Single(_local.Fake.Calls);
        Assert.Same(_local, _manager.ActiveFallbackEngine);
        Assert.Equal(VoiceServiceState.Offline, _manager.ServiceState);

        _edge.Fake.Failure = null;
        Say("leo", "vuelve internet");
        Assert.True(await WaitFor(() => _player.PlayedCount == 2));
        Assert.Null(_manager.ActiveFallbackEngine);
        Assert.Equal(VoiceServiceState.Ready, _manager.ServiceState);
    }

    [Fact]
    public async Task Blocked_engine_keeps_reading_with_fallback_without_retrying_the_blocked_one()
    {
        _edge.Fake.Failure = () => new SpeechServiceBlockedException("403");
        _manager.Start();
        Say("ana", "primero");
        Assert.True(await WaitFor(() => _player.PlayedCount == 1));
        Assert.True(_manager.IsBlocked);

        _edge.Fake.Failure = () => throw new Xunit.Sdk.XunitException("no debería reintentar Edge bloqueado");
        Say("leo", "segundo");
        Assert.True(await WaitFor(() => _player.PlayedCount == 2));
        Assert.Equal(2, _local.Fake.Calls.Count);
        Assert.True(_manager.IsBlocked);
    }

    [Fact]
    public async Task Without_fallback_a_failure_is_not_read()
    {
        _settings.UseFallbackEngines = false;
        _edge.Fake.Failure = () => new HttpRequestException("No such host is known.");
        _manager.Start();
        Say("ana", "sin internet");

        Assert.True(await WaitFor(() => _manager.ServiceState == VoiceServiceState.Offline));
        await Task.Delay(200);
        Assert.Equal(0, _player.PlayedCount);
        Assert.Empty(_local.Fake.Calls);
    }

    [Fact]
    public async Task Each_platform_can_have_its_own_voice()
    {
        _settings.PerPlatformVoice = true;
        _settings.SetPlatformVoiceId(TtsEngines.Edge, "tiktok", "es-MX-B");
        _settings.SetPlatformVoiceId(TtsEngines.Edge, "twitch", "en-US-D");
        _manager.Start();
        _manager.Enqueue(new TtsMessage("tiktok", "ana", "desde tiktok", DateTime.UtcNow) { SpeakerKey = "ana" });
        _manager.Enqueue(new TtsMessage("twitch", "leo", "desde twitch", DateTime.UtcNow) { SpeakerKey = "leo" });

        Assert.True(await WaitFor(() => _edge.Fake.Calls.Count == 2));
        Assert.Equal("es-MX-B", _edge.Fake.Calls.Single(c => c.Text.Contains("tiktok")).VoiceId);
        Assert.Equal("en-US-D", _edge.Fake.Calls.Single(c => c.Text.Contains("twitch")).VoiceId);

        // Sin voz propia para una plataforma, o con la opción apagada, se usa la general.
        _settings.PlatformVoiceIds.Clear();
        _settings.SetPlatformVoiceId(TtsEngines.Edge, "tiktok", "es-MX-B");
        _settings.PerPlatformVoice = false;
        _edge.Fake.ClearCalls();
        _manager.Enqueue(new TtsMessage("tiktok", "sol", "general", DateTime.UtcNow) { SpeakerKey = "sol" });
        Assert.True(await WaitFor(() => _edge.Fake.Calls.Count == 1));
        Assert.Equal("es-MX-A", _edge.Fake.Calls[0].VoiceId);
    }

    [Fact]
    public async Task Test_button_can_try_a_platform_voice()
    {
        _settings.PerPlatformVoice = true;
        _settings.SetPlatformVoiceId(TtsEngines.Edge, "twitch", "en-US-E");

        await _manager.PlaySampleAsync("prueba", CancellationToken.None, "twitch");
        await _manager.PlaySampleAsync("prueba", CancellationToken.None);

        Assert.Equal(["en-US-E", "es-MX-A"], _edge.Fake.Calls.Select(c => c.VoiceId));
    }

    [Fact]
    public async Task No_connection_is_a_soft_warning_not_a_block()
    {
        _edge.Fake.Failure = () => new HttpRequestException("No such host is known.");
        await _manager.CheckServiceAsync(CancellationToken.None);

        Assert.Equal(VoiceServiceState.Offline, _manager.ServiceState);
        Assert.False(_manager.IsBlocked);
        Assert.Contains("internet", _manager.ServiceMessage);
    }

    [Fact]
    public async Task Each_engine_uses_its_own_voice_and_pitch_support()
    {
        _settings.PitchPercent = 20;
        _manager.Start();
        Say("ana", "con edge");
        Assert.True(await WaitFor(() => _edge.Fake.Calls.Count == 1));
        Assert.Equal(20, _edge.Fake.Calls[0].Style.PitchPercent);

        _settings.Engine = TtsEngines.Windows;
        Say("leo", "con windows");
        Assert.True(await WaitFor(() => _local.Fake.Calls.Count == 1));
        Assert.Equal("win-1", _local.Fake.Calls[0].VoiceId);
        Assert.Equal(0, _local.Fake.Calls[0].Style.PitchPercent); // este motor no soporta tono
    }

    [Fact]
    public async Task Random_voice_keeps_one_voice_per_person_within_selected_languages()
    {
        _settings.RandomVoice = true;
        _settings.RandomLanguages = ["es", "en"];
        await _manager.ListVoicesAsync(CancellationToken.None);
        Assert.Equal(5, _manager.RandomVoicePool().Count);

        _manager.Start();
        foreach (var round in Enumerable.Range(0, 3))
        {
            foreach (var user in new[] { "ana", "leo", "sol", "max" })
            {
                Say(user, $"{user} ronda {round}");
            }
        }

        Assert.True(await WaitFor(() => _player.PlayedCount == 12));
        var voicesByUser = _edge.Fake.Calls
            .GroupBy(c => c.Text.Split(' ')[0])
            .ToDictionary(g => g.Key, g => g.Select(c => c.VoiceId).Distinct().ToList());
        Assert.All(voicesByUser.Values, v => Assert.Single(v));
        Assert.All(_edge.Fake.Calls, c => Assert.DoesNotContain("ja-", c.VoiceId));
    }

    [Fact]
    public async Task Random_voice_without_matching_voices_uses_the_fixed_one()
    {
        _settings.RandomVoice = true;
        _settings.RandomLanguages = ["fr"];
        await _manager.ListVoicesAsync(CancellationToken.None);
        _manager.Start();
        Say("ana", "sin voces en francés");

        Assert.True(await WaitFor(() => _edge.Fake.Calls.Count == 1));
        Assert.Equal("es-MX-A", _edge.Fake.Calls[0].VoiceId);
    }

    [Fact]
    public void Missing_microphone_is_reported_without_breaking()
    {
        _mic.FailOnStart = true;
        _settings.PauseWhenISpeak = true;
        _manager.Start();

        Assert.True(_manager.IsRunning);
        Assert.NotEmpty(_manager.MicrophoneError);
    }

    [Fact]
    public void Invalid_hotkeys_are_rejected_with_a_readable_message()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => _manager.UpdateHotkeys("patata", "Control+Alt+N"));
        Assert.Contains("silenciar", ex.Message);
    }
}
