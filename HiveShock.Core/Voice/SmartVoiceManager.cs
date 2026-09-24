using HiveShock.Logging;

namespace HiveShock.Voice;

/// <summary>
/// Orquesta Smart TTS: mic → <see cref="IVoiceActivityDetector"/> → <see cref="SmartTtsQueue"/> →
/// <see cref="ISpeechSynthesizer"/> → <see cref="IAudioPlayer"/>. Vive en BridgeRuntime (vida
/// larga, como Overlay/Goals), no en el IHost por sesión: el streamer debe poder probar la
/// voz o el mic sin estar conectado al live. Solo el chat en vivo se alimenta por sesión
/// (ver el hosted service que llama a <see cref="Enqueue"/>).
/// </summary>
public sealed class SmartVoiceManager : IDisposable
{
    private readonly IMicrophoneCapture _microphone;
    private readonly ISpeechSynthesizer _synthesizer;
    private readonly IAudioPlayer _player;
    private readonly IGlobalHotkeyListener _hotkeys;
    private readonly IVoiceActivityDetector _vad;
    private readonly TtsSettings _settings;

    private SmartTtsQueue _queue;
    private CancellationTokenSource? _runCts;
    private Task? _consumeTask;
    private CancellationTokenSource? _playbackCts;
    private bool _isMuted;

    public SmartVoiceManager(
        IMicrophoneCapture microphone,
        ISpeechSynthesizer synthesizer,
        IAudioPlayer player,
        IGlobalHotkeyListener hotkeys,
        IVoiceActivityDetector vad,
        TtsSettings settings)
    {
        _microphone = microphone;
        _synthesizer = synthesizer;
        _player = player;
        _hotkeys = hotkeys;
        _vad = vad;
        _settings = settings;
        _queue = new SmartTtsQueue(Math.Max(1, settings.MaxQueueLength));
        _player.Volume = settings.Volume;

        _vad.SpeakingChanged += OnStreamerSpeakingChanged;
        _hotkeys.MuteToggleRequested += () => IsMuted = !IsMuted;
    }

    public bool IsRunning { get; private set; }

    /// <summary>Config persistida (perfil de voz, atajo, volumen). La UI la lee/edita directo y llama Save().</summary>
    public TtsSettings Settings => _settings;

    public double Volume
    {
        get => _settings.Volume;
        set
        {
            _settings.Volume = Math.Clamp(value, 0, 1);
            _player.Volume = _settings.Volume;
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (_isMuted == value)
            {
                return;
            }

            _isMuted = value;
            if (value)
            {
                _playbackCts?.Cancel();
                _player.Stop();
            }

            MutedChanged?.Invoke(value);
        }
    }

    public bool IsStreamerSpeaking => _vad.IsSpeaking;

    public event Action<bool>? MutedChanged;
    public event Action<bool>? StreamerSpeakingChanged;

    /// <summary>Arranca captura de mic, atajo global y el consumidor de la cola. Independiente de Connect/Disconnect.</summary>
    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        _queue = new SmartTtsQueue(Math.Max(1, _settings.MaxQueueLength));
        _vad.Reset();
        _microphone.SamplesAvailable += OnMicSamples;
        _microphone.Start();
        _hotkeys.Start(_settings.MuteHotkey);

        _runCts = new CancellationTokenSource();
        _consumeTask = ConsumeLoopAsync(_runCts.Token);
        IsRunning = true;
        BridgeLog.Info("Smart TTS iniciado.");
    }

    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        _microphone.SamplesAvailable -= OnMicSamples;
        _microphone.Stop();
        _hotkeys.Stop();
        _playbackCts?.Cancel();
        _player.Stop();
        _runCts?.Cancel();
        _runCts?.Dispose();
        _runCts = null;
        _consumeTask = null;
        IsRunning = false;
        BridgeLog.Info("Smart TTS detenido.");
    }

    /// <summary>Empuja un comentario del chat a la cola. No hace nada si Smart TTS está apagado.</summary>
    public void Enqueue(TtsMessage message)
    {
        if (!_settings.Enabled || !IsRunning)
        {
            return;
        }

        _queue.TryEnqueue(message);
    }

    /// <summary>Cambia el atajo global de mute. Si el listener ya está corriendo, lo reaplica sin reiniciar el mic.</summary>
    public void UpdateMuteHotkey(string comboText)
    {
        _settings.MuteHotkey = comboText;
        if (IsRunning)
        {
            _hotkeys.Start(comboText);
        }
    }

    public Task<IReadOnlyList<VoiceProfile>> ListVoicesAsync(CancellationToken ct) =>
        _synthesizer.ListVoicesAsync(ct);

    /// <summary>Sintetiza y reproduce un texto de prueba fuera de la cola de chat (para el botón "Probar" de la UI).</summary>
    public Task PlaySampleAsync(string text, CancellationToken ct) =>
        SpeakAsync(new TtsMessage("test", "Tú", text, DateTime.UtcNow), ct);

    private void OnMicSamples(byte[] pcm) => _vad.PushSamples(pcm);

    private void OnStreamerSpeakingChanged(bool speaking)
    {
        if (speaking)
        {
            // El streamer empezó a hablar: corta lo que se esté leyendo ya mismo, no esperar a que termine.
            _playbackCts?.Cancel();
            _player.Stop();
        }

        StreamerSpeakingChanged?.Invoke(speaking);
    }

    private async Task ConsumeLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var message in _queue.ReadAllAsync(ct).ConfigureAwait(false))
            {
                await WaitUntilClearToSpeakAsync(ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                await SpeakAsync(message, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Detener Smart TTS cancela el lector del canal a propósito.
        }
    }

    private async Task WaitUntilClearToSpeakAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && (_vad.IsSpeaking || IsMuted))
        {
            await Task.Delay(100, ct).ConfigureAwait(false);
        }
    }

    private async Task SpeakAsync(TtsMessage message, CancellationToken ct)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _playbackCts = linkedCts;
        try
        {
            // Solo el Id importa para sintetizar; DisplayName/Locale son para la UI de selección de voz.
            var voice = new VoiceProfile(_settings.VoiceId, _settings.VoiceId, "");
            var audio = await _synthesizer.SynthesizeAsync(message.Text, voice, linkedCts.Token).ConfigureAwait(false);
            await using (audio.ConfigureAwait(false))
            {
                await _player.PlayAsync(audio, linkedCts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Cortado porque el streamer empezó a hablar (o se muteó); el mensaje se descarta, no se reintenta.
        }
        catch (Exception ex)
        {
            BridgeLog.Warn($"Smart TTS: {ex.Message}");
        }
        finally
        {
            _playbackCts = null;
        }
    }

    public void Dispose()
    {
        Stop();
        _vad.Dispose();
        _microphone.Dispose();
        _player.Dispose();
        _hotkeys.Dispose();
    }
}
