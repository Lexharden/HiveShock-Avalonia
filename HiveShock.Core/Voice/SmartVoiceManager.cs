using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading.Channels;
using HiveShock.Logging;

namespace HiveShock.Voice;

/// <summary>Estado del motor de voz elegido, para el aviso de la página Voz.</summary>
public enum VoiceServiceState
{
    Unknown,
    Checking,
    Ready,

    /// <summary>Fallo pasajero (sin internet, timeout): se reintenta solo con el siguiente mensaje.</summary>
    Offline,

    /// <summary>Microsoft rechazó el acceso (403): la lectura se detiene y la UI bloquea la sección.</summary>
    Blocked,
}

/// <summary>
/// Orquesta Smart TTS: chat → <see cref="TtsMessageFilter"/> → <see cref="SmartTtsQueue"/> →
/// <see cref="ISpeechSynthesizer"/> → <see cref="IAudioPlayer"/>, con el micrófono
/// (<see cref="IVoiceActivityDetector"/>) pausando la lectura mientras el streamer habla.
/// Dos bucles: uno sintetiza por adelantado (así no hay silencio entre mensajes) y otro
/// reproduce. Vive en BridgeRuntime (vida larga, como Overlay/Goals), no en el IHost por
/// sesión: el streamer debe poder probar la voz o el mic sin estar conectado al live.
/// </summary>
public sealed class SmartVoiceManager : IDisposable
{
    /// <summary>Por qué se cortó la frase que sonaba (decide si se repite o se descarta).</summary>
    private enum InterruptReason
    {
        None,
        StreamerSpoke,
        Skip,
        Mute,
        Clear,
    }

    /// <summary>Cómo terminó una reproducción.</summary>
    private enum PlayOutcome
    {
        Completed,
        InterruptedBySpeech,
        Discarded,
    }

    /// <summary>Veces que se repite una frase cortada porque el streamer habló (evita bucles si habla mucho).</summary>
    private const int MaxReplaysPerMessage = 2;

    /// <summary>Silencio extra tras dejar de hablar antes de repetir, para no pisar la siguiente frase del streamer.</summary>
    private static readonly TimeSpan ReplayCalmDelay = TimeSpan.FromMilliseconds(600);

    private const double LevelFullScaleRms = 4000;

    private readonly IMicrophoneCapture _microphone;
    private readonly TtsEngineRegistry _engines;
    private readonly IAudioPlayer _player;
    private readonly IGlobalHotkeyListener _hotkeys;
    private readonly IVoiceActivityDetector _vad;
    private readonly TtsSettings _settings;
    private readonly TtsMessageFilter _filter = new();

    private readonly object _cancelGate = new();
    private readonly object _micGate = new();
    private readonly SemaphoreSlim _playGate = new(1, 1);

    private SmartTtsQueue _queue;
    private Channel<PreparedSpeech> _ready = CreateReadyChannel();
    private CancellationTokenSource? _runCts;
    private CancellationTokenSource? _playbackCts;
    private InterruptReason _interruptReason;
    private bool _isMuted;
    private bool _micTestActive;
    private bool _micSubscribed;
    private string? _micDeviceInUse;
    private string? _blockedEngineId;
    private readonly Dictionary<string, IReadOnlyList<VoiceProfile>> _voiceCache = new(StringComparer.Ordinal);
    /// <summary>Sube con cada "vaciar": lo que ya estaba en camino (sintetizándose o esperando) se descarta.</summary>
    private int _generation;
    private int _spokenCount;
    private int _filteredCount;

    public SmartVoiceManager(
        IMicrophoneCapture microphone,
        TtsEngineRegistry engines,
        IAudioPlayer player,
        IGlobalHotkeyListener hotkeys,
        IVoiceActivityDetector vad,
        TtsSettings settings)
    {
        _microphone = microphone;
        _engines = engines;
        _player = player;
        _hotkeys = hotkeys;
        _vad = vad;
        _settings = settings;

        _queue = new SmartTtsQueue(Math.Max(1, settings.MaxQueueLength));
        _player.Volume = settings.Volume;
        _player.DeviceId = settings.OutputDeviceId;
        _vad.Threshold = SensitivityToThreshold(settings.MicSensitivity);

        _vad.SpeakingChanged += OnStreamerSpeakingChanged;
        _hotkeys.MuteToggleRequested += () => IsMuted = !IsMuted;
        _hotkeys.SkipRequested += SkipCurrent;
    }

    /// <summary>Cualquier cambio de estado visible (mute, voz del streamer, servicio, lo que se está leyendo). Llega desde cualquier hilo.</summary>
    public event Action? StateChanged;

    /// <summary>Config persistida. La UI la edita directo, llama al Apply… correspondiente y luego Save().</summary>
    public TtsSettings Settings => _settings;

    public bool IsRunning { get; private set; }

    /// <summary>Todos los motores de voz de este equipo (la UI arma el desplegable con ellos).</summary>
    public TtsEngineRegistry Engines => _engines;

    /// <summary>El motor elegido en la config, o el predeterminado si ese no existe o no está disponible.</summary>
    public ITtsEngine CurrentEngine => _engines.Resolve(_settings.Engine);

    public VoiceServiceState ServiceState { get; private set; } = VoiceServiceState.Unknown;

    /// <summary>Explicación para el streamer del estado actual del servicio (vacío si todo va bien).</summary>
    public string ServiceMessage { get; private set; } = "";

    /// <summary>
    /// Motor de respaldo con el que se leyó el último mensaje porque el elegido falló, o null si
    /// el elegido funciona. La UI lo muestra ("Mientras tanto se usan las voces de Windows").
    /// </summary>
    public ITtsEngine? ActiveFallbackEngine { get; private set; }

    /// <summary>True si el proveedor del motor elegido bloqueó el acceso (p. ej. Microsoft con Edge).</summary>
    public bool IsBlocked => ServiceState == VoiceServiceState.Blocked &&
                             string.Equals(_blockedEngineId, CurrentEngine.Id, StringComparison.OrdinalIgnoreCase);

    public string MicrophoneError { get; private set; } = "";
    public string OutputError { get; private set; } = "";
    public string HotkeyError { get; private set; } = "";

    public bool IsMicrophoneActive => _microphone.IsCapturing;
    public bool IsMicTestActive => _micTestActive;

    /// <summary>0..1 para el medidor; <see cref="MicThreshold"/> en la misma escala.</summary>
    public double MicLevel => _microphone.IsCapturing ? _vad.Level : 0;
    public double MicThreshold => _vad.Threshold;

    public bool IsStreamerSpeaking => _microphone.IsCapturing && _vad.IsSpeaking;

    public int QueueCount => _queue.Count + _ready.Reader.Count;
    public int SpokenCount => Volatile.Read(ref _spokenCount);
    public int FilteredCount => Volatile.Read(ref _filteredCount);
    public string LastFilteredReason { get; private set; } = "";

    /// <summary>La frase que suena ahora mismo, o vacío.</summary>
    public string NowSpeaking { get; private set; } = "";

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
                CancelPlayback(InterruptReason.Mute);
            }

            BridgeLog.Info(value ? "Smart TTS silenciado." : "Smart TTS reactivado.");
            RaiseStateChanged();
        }
    }

    /// <summary>Arranca micrófono, atajos y los bucles de lectura. Independiente de Connect/Disconnect.</summary>
    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        _filter.Reset();
        _queue = new SmartTtsQueue(Math.Max(1, _settings.MaxQueueLength));
        _ready = CreateReadyChannel();
        _runCts = new CancellationTokenSource();
        var token = _runCts.Token;
        IsRunning = true;

        ApplyHotkeys();
        ApplyMicrophone();
        _ = SynthesizeLoopAsync(_queue, _ready, token);
        _ = PlayLoopAsync(_ready, token);

        BridgeLog.Info("Smart TTS iniciado.");
        RaiseStateChanged();
    }

    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        IsRunning = false;
        _runCts?.Cancel();
        _runCts?.Dispose();
        _runCts = null;
        CancelPlayback();
        _queue.Clear();
        _ready.Writer.TryComplete();
        _hotkeys.Stop();
        HotkeyError = "";
        ApplyMicrophone();

        BridgeLog.Info("Smart TTS detenido.");
        RaiseStateChanged();
    }

    /// <summary>Filtra y encola algo del live. No hace nada si Smart TTS está apagado o bloqueado.</summary>
    public void Enqueue(TtsMessage message)
    {
        if (!_settings.Enabled || !IsRunning || (IsBlocked && !CanReadWithFallback))
        {
            return;
        }

        var result = _filter.Evaluate(message, _settings, DateTime.UtcNow);
        if (!result.Accepted)
        {
            RecordFiltered(result.SkipReason ?? "");
            return;
        }

        _queue.TryEnqueue(message with { Text = result.SpokenText! });
    }

    /// <summary>Corta la frase que se está leyendo y pasa a la siguiente (no se repite).</summary>
    public void SkipCurrent() => CancelPlayback(InterruptReason.Skip);

    /// <summary>Descarta todo lo que está esperando turno (y lo que suena ahora).</summary>
    public void ClearQueue()
    {
        Interlocked.Increment(ref _generation);
        _queue.Clear();
        while (_ready.Reader.TryRead(out var prepared))
        {
            prepared.Audio.Dispose();
        }

        CancelPlayback(InterruptReason.Clear);
        RaiseStateChanged();
    }

    /// <summary>Reaplica atajos de silenciar/saltar. Lanza InvalidOperationException con texto para el streamer si no se entienden.</summary>
    public void UpdateHotkeys(string muteCombo, string skipCombo)
    {
        muteCombo = (muteCombo ?? "").Trim();
        skipCombo = (skipCombo ?? "").Trim();
        if (muteCombo.Length > 0 && !_hotkeys.IsValidCombo(muteCombo))
        {
            throw new InvalidOperationException($"No se entiende el atajo para silenciar: \"{muteCombo}\". Usa algo como Control+Alt+M.");
        }

        if (skipCombo.Length > 0 && !_hotkeys.IsValidCombo(skipCombo))
        {
            throw new InvalidOperationException($"No se entiende el atajo para saltar: \"{skipCombo}\". Usa algo como Control+Alt+N.");
        }

        _settings.MuteHotkey = muteCombo;
        _settings.SkipHotkey = skipCombo;
        if (IsRunning)
        {
            ApplyHotkeys();
        }
    }

    /// <summary>Abre/cierra/cambia el micrófono según la config actual (pausar al hablar, dispositivo, sensibilidad, prueba).</summary>
    public void ApplyMicrophone()
    {
        lock (_micGate)
        {
            _vad.Threshold = SensitivityToThreshold(_settings.MicSensitivity);
            var wanted = (IsRunning && _settings.PauseWhenISpeak) || _micTestActive;
            var device = _settings.MicrophoneDeviceId ?? "";

            if (_microphone.IsCapturing && (!wanted || device != _micDeviceInUse))
            {
                StopMicrophone();
            }

            if (!wanted)
            {
                MicrophoneError = "";
                RaiseStateChanged();
                return;
            }

            if (_microphone.IsCapturing)
            {
                return;
            }

            try
            {
                _microphone.DeviceId = device;
                if (!_micSubscribed)
                {
                    _microphone.SamplesAvailable += OnMicSamples;
                    _micSubscribed = true;
                }

                _microphone.Start();
                _micDeviceInUse = device;
                MicrophoneError = "";
            }
            catch (Exception ex)
            {
                StopMicrophone();
                MicrophoneError = ListMicrophones().Count == 0
                    ? "No se encontró ningún micrófono conectado. La voz leerá sin pausarse cuando hables."
                    : "No se pudo abrir el micrófono elegido. Revisa que esté conectado y que Windows " +
                      "permita a las aplicaciones usar el micrófono (Configuración > Privacidad > Micrófono).";
                BridgeLog.Warn($"Smart TTS micrófono: {ex.Message}");
            }

            RaiseStateChanged();
        }
    }

    /// <summary>Enciende el micrófono solo para ver el medidor y calibrar la sensibilidad, aunque Smart TTS esté apagado.</summary>
    public void SetMicTest(bool active)
    {
        _micTestActive = active;
        ApplyMicrophone();
    }

    public void ApplyOutputDevice()
    {
        _player.DeviceId = _settings.OutputDeviceId ?? "";
        OutputError = "";
        RaiseStateChanged();
    }

    public IReadOnlyList<AudioDevice> ListMicrophones() => SafeList(_microphone.ListDevices);
    public IReadOnlyList<AudioDevice> ListOutputs() => SafeList(_player.ListDevices);

    /// <summary>Voces del motor elegido. Si Microsoft bloqueó el acceso, marca el estado y relanza.</summary>
    public async Task<IReadOnlyList<VoiceProfile>> ListVoicesAsync(CancellationToken ct)
    {
        try
        {
            var engine = CurrentEngine;
            var voices = await engine.Synthesizer.ListVoicesAsync(ct).ConfigureAwait(false);
            lock (_voiceCache)
            {
                _voiceCache[engine.Id] = voices;
            }

            return voices;
        }
        catch (SpeechServiceBlockedException ex)
        {
            MarkBlocked(ex);
            throw;
        }
    }

    /// <summary>
    /// Prueba el motor elegido con una frase corta (sin reproducirla) y actualiza
    /// <see cref="ServiceState"/>. Así el bloqueo se detecta al abrir la app, no en pleno directo.
    /// </summary>
    public async Task CheckServiceAsync(CancellationToken ct)
    {
        SetServiceState(VoiceServiceState.Checking, "Comprobando el servicio de voces…");
        try
        {
            var engine = CurrentEngine;
            if (!engine.IsAvailable)
            {
                SetServiceState(VoiceServiceState.Offline, engine.UnavailableReason);
                return;
            }

            await engine.CheckAsync(_settings.GetVoiceId(engine.Id), ct).ConfigureAwait(false);
            SetServiceState(VoiceServiceState.Ready, "");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (SpeechServiceBlockedException ex)
        {
            MarkBlocked(ex);
        }
        catch (Exception ex)
        {
            SetServiceState(VoiceServiceState.Offline, FriendlyError(ex));
        }
    }

    /// <summary>
    /// Sintetiza y reproduce un texto de prueba fuera de la cola (botón "Probar"). Ignora el mute
    /// y no usa respaldo: así el streamer ve si el motor elegido funciona de verdad. Con
    /// <paramref name="portId"/> prueba la voz de esa plataforma.
    /// </summary>
    public async Task PlaySampleAsync(string text, CancellationToken ct, string? portId = null)
    {
        Stream audio;
        try
        {
            var source = portId == null ? null : new TtsMessage(portId, "", text, DateTime.UtcNow);
            audio = await SynthesizeWithAsync(CurrentEngine, text, source, ct).ConfigureAwait(false);
            MarkServiceReady();
        }
        catch (SpeechServiceBlockedException ex)
        {
            MarkBlocked(ex);
            throw new InvalidOperationException(ServiceMessage);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetServiceState(VoiceServiceState.Offline, FriendlyError(ex));
            throw new InvalidOperationException(ServiceMessage);
        }

        await using (audio.ConfigureAwait(false))
        {
            await PlayAsync(text, audio, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Hay algún otro motor disponible con el que seguir leyendo si el elegido falla.</summary>
    private bool CanReadWithFallback => _settings.UseFallbackEngines && FallbackEngines(CurrentEngine).Count > 0;

    /// <summary>Resto de motores disponibles, en el orden del registro (preferencia).</summary>
    private List<ITtsEngine> FallbackEngines(ITtsEngine primary) =>
        _engines.Available.Where(e => !string.Equals(e.Id, primary.Id, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>
    /// Sintetiza con el motor elegido y, si falla y el respaldo está activado, con los demás en
    /// orden. El fallo del elegido se refleja igual en el estado (aviso o bloqueo) aunque el
    /// mensaje se lea con otro motor. Si el elegido ya está bloqueado, ni se intenta.
    /// </summary>
    private async Task<Stream> SynthesizeWithFallbackAsync(TtsMessage message, CancellationToken ct)
    {
        var primary = CurrentEngine;
        Exception? primaryError = null;
        if (!IsBlocked)
        {
            try
            {
                var audio = await SynthesizeWithAsync(primary, message.Text, message, ct).ConfigureAwait(false);
                MarkServiceReady();
                SetActiveFallback(null);
                return audio;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (_settings.UseFallbackEngines)
            {
                primaryError = ex;
                if (ex is SpeechServiceBlockedException blocked)
                {
                    MarkBlocked(blocked);
                }
                else
                {
                    SetServiceState(VoiceServiceState.Offline, FriendlyError(ex));
                }
            }
        }

        foreach (var fallback in FallbackEngines(primary))
        {
            try
            {
                var audio = await SynthesizeWithAsync(fallback, message.Text, message, ct).ConfigureAwait(false);
                SetActiveFallback(fallback);
                return audio;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                BridgeLog.Warn($"Smart TTS respaldo {fallback.Id}: {ex.Message}");
            }
        }

        throw primaryError ?? new InvalidOperationException("Ningún tipo de voz pudo leer el mensaje.");
    }

    /// <summary>Sintetiza con ese motor y la voz que toca: la de la plataforma, la general o una del sorteo.</summary>
    private Task<Stream> SynthesizeWithAsync(ITtsEngine engine, string text, TtsMessage? source, CancellationToken ct)
    {
        var voiceId = PickVoiceId(engine, source);
        var style = new SpeechStyle(_settings.RatePercent, engine.SupportsPitch ? _settings.PitchPercent : 0);
        return engine.Synthesizer.SynthesizeAsync(text, new VoiceProfile(voiceId, voiceId, ""), style, ct);
    }

    private void SetActiveFallback(ITtsEngine? engine)
    {
        if (!ReferenceEquals(ActiveFallbackEngine, engine))
        {
            ActiveFallbackEngine = engine;
            if (engine != null)
            {
                BridgeLog.Warn($"Smart TTS: leyendo con {engine.DisplayName} mientras el tipo de voz elegido no funciona.");
            }

            RaiseStateChanged();
        }
    }

    /// <summary>Voces que entran en el sorteo con el motor y los idiomas actuales.</summary>
    public IReadOnlyList<VoiceProfile> RandomVoicePool()
    {
        IReadOnlyList<VoiceProfile>? voices;
        lock (_voiceCache)
        {
            _voiceCache.TryGetValue(CurrentEngine.Id, out voices);
        }

        if (voices == null || voices.Count == 0)
        {
            return [];
        }

        var languages = new HashSet<string>(_settings.RandomLanguages, StringComparer.OrdinalIgnoreCase);
        var excluded = new HashSet<string>(_settings.RandomExcludedVoices, StringComparer.Ordinal);
        return voices
            .Where(v => languages.Contains(VoiceLanguages.LanguageCode(v.Locale)) && !excluded.Contains(v.Id))
            .ToList();
    }

    /// <summary>
    /// Voz fija: la de la plataforma del mensaje si hay voz por plataforma, si no la general del
    /// motor. Voz aleatoria (tiene prioridad, solo en el motor elegido): por persona (hash estable
    /// del usuario, así cada quien suena siempre igual) o al azar; sin voces en el sorteo, la fija.
    /// </summary>
    private string PickVoiceId(ITtsEngine engine, TtsMessage? source)
    {
        var fixedId = _settings.GetVoiceId(engine.Id);
        if (_settings.PerPlatformVoice && source != null &&
            _settings.GetPlatformVoiceId(engine.Id, source.PortId) is { Length: > 0 } platformId)
        {
            fixedId = platformId;
        }

        if (!_settings.RandomVoice || !ReferenceEquals(engine, CurrentEngine))
        {
            return fixedId;
        }

        var pool = RandomVoicePool();
        if (pool.Count == 0)
        {
            return fixedId;
        }

        var who = source == null ? "" : $"{source.PortId}:{(source.SpeakerKey.Length > 0 ? source.SpeakerKey : source.Speaker)}";
        var index = _settings.RandomVoicePerUser && who.Length > 1
            ? (int)(StableHash(who.ToLowerInvariant()) % (uint)pool.Count)
            : Random.Shared.Next(pool.Count);
        return pool[index].Id;
    }

    /// <summary>FNV-1a: string.GetHashCode cambia en cada ejecución y la voz de cada persona debe mantenerse.</summary>
    private static uint StableHash(string text)
    {
        var hash = 2166136261u;
        foreach (var c in text)
        {
            hash = (hash ^ c) * 16777619u;
        }

        return hash;
    }

    private async Task SynthesizeLoopAsync(SmartTtsQueue queue, Channel<PreparedSpeech> ready, CancellationToken ct)
    {
        try
        {
            await foreach (var message in queue.ReadAllAsync(ct).ConfigureAwait(false))
            {
                var generation = Volatile.Read(ref _generation);
                while (IsMuted && !ct.IsCancellationRequested)
                {
                    await Task.Delay(150, ct).ConfigureAwait(false);
                }

                if ((IsBlocked && !CanReadWithFallback) || generation != Volatile.Read(ref _generation))
                {
                    continue;
                }

                if (IsTooOld(message))
                {
                    RecordFiltered("llevaba demasiado tiempo esperando");
                    continue;
                }

                Stream audio;
                try
                {
                    audio = await SynthesizeWithFallbackAsync(message, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (SpeechServiceBlockedException ex)
                {
                    MarkBlocked(ex);
                    queue.Clear();
                    continue;
                }
                catch (Exception ex)
                {
                    // Bloqueado + respaldo fallido: se mantiene el aviso de bloqueo, que es el importante.
                    if (!IsBlocked)
                    {
                        SetServiceState(VoiceServiceState.Offline, FriendlyError(ex));
                    }

                    BridgeLog.Warn($"Smart TTS: {ex.Message}");
                    continue;
                }

                try
                {
                    // Capacidad 1: se sintetiza como mucho un mensaje por delante del que suena.
                    await ready.Writer.WriteAsync(new PreparedSpeech(message, audio, generation), ct).ConfigureAwait(false);
                }
                catch
                {
                    await audio.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Detener Smart TTS cancela el lector a propósito.
        }
        catch (ChannelClosedException)
        {
            // Stop() cerró el canal de "listos".
        }
        catch (Exception ex)
        {
            BridgeLog.Error($"Smart TTS (síntesis) se detuvo: {ex.Message}");
        }
    }

    private async Task PlayLoopAsync(Channel<PreparedSpeech> ready, CancellationToken ct)
    {
        try
        {
            await foreach (var prepared in ready.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                await using (prepared.Audio.ConfigureAwait(false))
                {
                    await PlayPreparedAsync(prepared, ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Detener Smart TTS cancela el lector a propósito.
        }
        catch (Exception ex)
        {
            BridgeLog.Error($"Smart TTS (reproducción) se detuvo: {ex.Message}");
        }
    }

    /// <summary>
    /// Reproduce una frase ya sintetizada. Si el streamer empieza a hablar a mitad, se corta y,
    /// con <see cref="TtsSettings.ReplayAfterInterruption"/>, vuelve a sonar desde el principio
    /// cuando termina de hablar (hasta <see cref="MaxReplaysPerMessage"/> veces y mientras no
    /// sea demasiado vieja). Saltar, silenciar o vaciar la descartan.
    /// </summary>
    private async Task PlayPreparedAsync(PreparedSpeech prepared, CancellationToken ct)
    {
        var audio = prepared.Audio.CanSeek ? prepared.Audio : await BufferAsync(prepared.Audio, ct).ConfigureAwait(false);
        for (var replays = 0; ; replays++)
        {
            await WaitUntilClearToSpeakAsync(ct).ConfigureAwait(false);
            if (replays > 0)
            {
                // Tras una interrupción: un momento de calma, por si el streamer solo hizo una pausa.
                await Task.Delay(ReplayCalmDelay, ct).ConfigureAwait(false);
                await WaitUntilClearToSpeakAsync(ct).ConfigureAwait(false);
            }

            if (prepared.Generation != Volatile.Read(ref _generation))
            {
                return;
            }

            if (IsTooOld(prepared.Message))
            {
                RecordFiltered(replays == 0
                    ? "llevaba demasiado tiempo esperando"
                    : "se cortó porque hablaste y ya era muy viejo para repetirlo");
                return;
            }

            audio.Position = 0;
            var outcome = await PlayAsync(prepared.Message.Text, audio, ct).ConfigureAwait(false);
            if (outcome == PlayOutcome.Completed)
            {
                Interlocked.Increment(ref _spokenCount);
                return;
            }

            if (outcome != PlayOutcome.InterruptedBySpeech || !_settings.ReplayAfterInterruption)
            {
                return;
            }

            if (replays >= MaxReplaysPerMessage)
            {
                RecordFiltered("se cortó varias veces porque hablaste");
                return;
            }

            BridgeLog.Info("Smart TTS: mensaje cortado porque hablaste; se repetirá cuando termines.");
        }
    }

    private static async Task<Stream> BufferAsync(Stream audio, CancellationToken ct)
    {
        var copy = new MemoryStream();
        await audio.CopyToAsync(copy, ct).ConfigureAwait(false);
        copy.Position = 0;
        return copy;
    }

    private async Task WaitUntilClearToSpeakAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && (IsMuted || (_settings.PauseWhenISpeak && IsStreamerSpeaking)))
        {
            await Task.Delay(100, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Reproduce con exclusión mutua (cola y botón Probar nunca suenan a la vez) y dice cómo terminó.</summary>
    private async Task<PlayOutcome> PlayAsync(string text, Stream audio, CancellationToken ct)
    {
        await _playGate.WaitAsync(ct).ConfigureAwait(false);
        var playback = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lock (_cancelGate)
        {
            _playbackCts = playback;
            _interruptReason = InterruptReason.None;
        }

        NowSpeaking = text;
        RaiseStateChanged();
        try
        {
            await _player.PlayAsync(audio, playback.Token).ConfigureAwait(false);
            if (OutputError.Length > 0)
            {
                OutputError = "";
            }

            return PlayOutcome.Completed;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Cortado: solo si fue porque el streamer habló vale la pena repetirlo.
            lock (_cancelGate)
            {
                return _interruptReason == InterruptReason.StreamerSpoke ? PlayOutcome.InterruptedBySpeech : PlayOutcome.Discarded;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            OutputError = "No se pudo reproducir el audio en la salida elegida. Prueba con \"Predeterminada de Windows\".";
            BridgeLog.Warn($"Smart TTS salida de audio: {ex.Message}");
            return PlayOutcome.Discarded;
        }
        finally
        {
            lock (_cancelGate)
            {
                _playbackCts = null;
            }

            playback.Dispose();
            NowSpeaking = "";
            _playGate.Release();
            RaiseStateChanged();
        }
    }

    private void CancelPlayback(InterruptReason reason = InterruptReason.Skip)
    {
        lock (_cancelGate)
        {
            if (_playbackCts != null)
            {
                _interruptReason = reason;
            }

            try
            {
                _playbackCts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // ya terminó
            }
        }

        _player.Stop();
    }

    private void OnMicSamples(byte[] pcm)
    {
        try
        {
            _vad.PushSamples(pcm);
        }
        catch (Exception ex)
        {
            BridgeLog.Warn($"Smart TTS detector de voz: {ex.Message}");
        }
    }

    private void OnStreamerSpeakingChanged(bool speaking)
    {
        if (speaking && _settings.PauseWhenISpeak && IsRunning)
        {
            // El streamer empezó a hablar: corta lo que se esté leyendo ya mismo. Si está activado
            // «repetir», la frase vuelve a sonar entera cuando termine de hablar.
            CancelPlayback(InterruptReason.StreamerSpoke);
        }

        RaiseStateChanged();
    }

    private void StopMicrophone()
    {
        if (_micSubscribed)
        {
            _microphone.SamplesAvailable -= OnMicSamples;
            _micSubscribed = false;
        }

        try
        {
            _microphone.Stop();
        }
        catch (Exception ex)
        {
            BridgeLog.Warn($"Smart TTS micrófono: {ex.Message}");
        }

        _micDeviceInUse = null;
        _vad.Reset();
    }

    private void ApplyHotkeys()
    {
        try
        {
            _hotkeys.Start(_settings.MuteHotkey, _settings.SkipHotkey);
            HotkeyError = "";
        }
        catch (Exception ex)
        {
            HotkeyError = "No se pudieron activar los atajos de teclado. Los botones de esta página siguen funcionando.";
            BridgeLog.Warn($"Smart TTS atajos: {ex.Message}");
        }
    }

    private bool IsTooOld(TtsMessage message) =>
        DateTime.UtcNow - message.ReceivedUtc > TimeSpan.FromSeconds(_settings.MaxMessageAgeSeconds);

    private void RecordFiltered(string reason)
    {
        Interlocked.Increment(ref _filteredCount);
        LastFilteredReason = reason;
        RaiseStateChanged();
    }

    private void MarkServiceReady()
    {
        if (ServiceState != VoiceServiceState.Ready)
        {
            SetServiceState(VoiceServiceState.Ready, "");
        }
    }

    private void MarkBlocked(SpeechServiceBlockedException ex)
    {
        _blockedEngineId = CurrentEngine.Id;
        BridgeLog.Error($"Smart TTS bloqueado: {ex.Message}");
        SetServiceState(VoiceServiceState.Blocked, ex.Message);
        _queue.Clear();
        CancelPlayback();
    }

    private void SetServiceState(VoiceServiceState state, string message)
    {
        ServiceState = state;
        ServiceMessage = message;
        RaiseStateChanged();
    }

    private void RaiseStateChanged()
    {
        try
        {
            StateChanged?.Invoke();
        }
        catch (Exception ex)
        {
            BridgeLog.Warn($"Smart TTS UI: {ex.Message}");
        }
    }

    private static IReadOnlyList<AudioDevice> SafeList(Func<IReadOnlyList<AudioDevice>> list)
    {
        try
        {
            return list();
        }
        catch (Exception ex)
        {
            BridgeLog.Warn($"Smart TTS dispositivos: {ex.Message}");
            return [];
        }
    }

    /// <summary>Sensibilidad 0..100 → umbral en la escala 0..1 del medidor (100 = capta voz muy baja).</summary>
    private static double SensitivityToThreshold(int sensitivity)
    {
        var rms = 2000 - Math.Clamp(sensitivity, 0, 100) * 19;
        return rms / LevelFullScaleRms;
    }

    private static string FriendlyError(Exception ex) => ex switch
    {
        TimeoutException t => t.Message + " Se reintentará con el siguiente mensaje.",
        HttpRequestException or WebSocketException or SocketException or IOException =>
            "No hay conexión con el servicio de voces. Revisa tu internet; se reintentará con el siguiente mensaje.",
        InvalidOperationException => ex.Message,
        _ => $"El servicio de voces falló: {ex.Message}",
    };

    private static Channel<PreparedSpeech> CreateReadyChannel() =>
        Channel.CreateBounded<PreparedSpeech>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.Wait,
        });

    public void Dispose()
    {
        Stop();
        _micTestActive = false;
        ApplyMicrophone();
        _vad.Dispose();
        _microphone.Dispose();
        _player.Dispose();
        _hotkeys.Dispose();
        foreach (var engine in _engines.All)
        {
            // Piper tiene procesos vivos: se cierran aquí, no al terminar el proceso de HiveShock.
            (engine as IDisposable)?.Dispose();
        }

        _playGate.Dispose();
    }

    private sealed record PreparedSpeech(TtsMessage Message, Stream Audio, int Generation);
}
