using HiveShock.Voice;

namespace HiveShock.Tests.Voice;

/// <summary>Sintetizador que registra qué se pidió y devuelve audio falso al instante.</summary>
internal sealed class FakeSynthesizer(IReadOnlyList<VoiceProfile>? voices = null) : ISpeechSynthesizer
{
    private readonly object _gate = new();
    private readonly List<(string Text, string VoiceId, SpeechStyle Style)> _calls = [];

    public IReadOnlyList<VoiceProfile> Voices { get; set; } = voices ?? [];

    /// <summary>Si se asigna, cada síntesis lanza esta excepción (403, sin internet…).</summary>
    public Func<Exception>? Failure { get; set; }

    public IReadOnlyList<(string Text, string VoiceId, SpeechStyle Style)> Calls
    {
        get
        {
            lock (_gate)
            {
                return _calls.ToList();
            }
        }
    }

    public void ClearCalls()
    {
        lock (_gate)
        {
            _calls.Clear();
        }
    }

    public Task<Stream> SynthesizeAsync(string text, VoiceProfile voice, SpeechStyle style, CancellationToken ct)
    {
        if (Failure != null)
        {
            throw Failure();
        }

        lock (_gate)
        {
            _calls.Add((text, voice.Id, style));
        }

        return Task.FromResult<Stream>(new MemoryStream([1, 2, 3]));
    }

    public Task<IReadOnlyList<VoiceProfile>> ListVoicesAsync(CancellationToken ct)
    {
        if (Failure != null)
        {
            throw Failure();
        }

        return Task.FromResult(Voices);
    }
}

internal sealed class FakeEngine(
    string id,
    bool requiresInternet,
    FakeSynthesizer? synthesizer = null,
    bool supportsPitch = true,
    bool available = true) : TtsEngineBase
{
    public override string Id => id;
    public override string DisplayName => $"Voces {id}";
    public override string Description => $"Motor de prueba {id}";
    public override bool RequiresInternet => requiresInternet;
    public override bool SupportsPitch => supportsPitch;
    public override bool IsAvailable => available;
    public override string UnavailableReason => available ? "" : $"{id} no está instalado";
    public override ISpeechSynthesizer Synthesizer => Fake;
    public FakeSynthesizer Fake { get; } = synthesizer ?? new FakeSynthesizer();
}

/// <summary>Reproductor sin sonido. Con <see cref="PlayDuration"/> simula frases largas que se pueden cortar.</summary>
internal sealed class FakePlayer : IAudioPlayer
{
    private int _played;
    private int _started;

    public TimeSpan PlayDuration { get; set; } = TimeSpan.Zero;

    /// <summary>Reproducciones que terminaron enteras.</summary>
    public int PlayedCount => Volatile.Read(ref _played);

    /// <summary>Reproducciones empezadas (incluye las cortadas y las repeticiones).</summary>
    public int StartedCount => Volatile.Read(ref _started);
    public bool IsPlaying { get; private set; }
    public double Volume { get; set; }
    public string DeviceId { get; set; } = "";

    public IReadOnlyList<AudioDevice> ListDevices() => [new AudioDevice("out-1", "Altavoces de prueba")];

    public async Task PlayAsync(Stream audio, CancellationToken ct)
    {
        Interlocked.Increment(ref _started);
        IsPlaying = true;
        try
        {
            if (PlayDuration > TimeSpan.Zero)
            {
                await Task.Delay(PlayDuration, ct).ConfigureAwait(false);
            }

            Interlocked.Increment(ref _played);
        }
        finally
        {
            IsPlaying = false;
        }
    }

    public void Stop()
    {
    }

    public void Dispose()
    {
    }
}

internal sealed class FakeMicrophone : IMicrophoneCapture
{
    public bool FailOnStart { get; set; }
    public int SampleRate => 16000;
    public bool IsCapturing { get; private set; }
    public string DeviceId { get; set; } = "";

    public event Action<byte[]>? SamplesAvailable;

    public IReadOnlyList<AudioDevice> ListDevices() => [new AudioDevice("mic-1", "Micrófono de prueba")];

    public void Start()
    {
        if (FailOnStart)
        {
            throw new InvalidOperationException("sin micrófono");
        }

        IsCapturing = true;
    }

    public void Stop() => IsCapturing = false;

    public void Emit(byte[] pcm) => SamplesAvailable?.Invoke(pcm);

    public void Dispose() => Stop();
}

internal sealed class FakeHotkeys : IGlobalHotkeyListener
{
    public bool IsListening { get; private set; }

    public event Action? MuteToggleRequested;
    public event Action? SkipRequested;

    public bool IsValidCombo(string comboText) => comboText.Contains('+');

    public void Start(string muteCombo, string skipCombo) => IsListening = true;

    public void Stop() => IsListening = false;

    public void PressMute() => MuteToggleRequested?.Invoke();

    public void PressSkip() => SkipRequested?.Invoke();

    public void Dispose() => Stop();
}

internal sealed class FakeVad : IVoiceActivityDetector
{
    public bool IsSpeaking { get; private set; }
    public double Level => IsSpeaking ? 0.8 : 0;
    public double Threshold { get; set; }

    public event Action<bool>? SpeakingChanged;

    public void PushSamples(byte[] pcm)
    {
    }

    public void SetSpeaking(bool speaking)
    {
        IsSpeaking = speaking;
        SpeakingChanged?.Invoke(speaking);
    }

    public void Reset() => SetSpeaking(false);

    public void Dispose()
    {
    }
}
