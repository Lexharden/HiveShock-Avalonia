using HiveShock.Logging;
using WebRtcVadSharp;

namespace HiveShock.Voice;

/// <summary>
/// Implementación de <see cref="IVoiceActivityDetector"/> para escritorio. Dos etapas:
/// 1) un noise gate barato (energía RMS) que corre siempre, para no gastar CPU en
///    silencio ni molestar al VAD con ruido de fondo constante; su umbral es la
///    "sensibilidad" que el streamer ajusta en la página Voz;
/// 2) si hay energía y estamos en Windows, se confirma con WebRTC VAD (más preciso,
///    pero el paquete solo trae binario nativo para Windows hoy). En Mac/Linux se
///    confía solo en el noise gate hasta que se elija un VAD portable para esos SO.
/// Espera PCM 16-bit mono a 16 kHz; la captura de micrófono debe entregarlo ya en ese
/// formato (no se resamplea aquí). Vive en HiveShock.Desktop (no en Core) para que
/// Android no arrastre el binario nativo de WebRTC VAD sin necesitarlo.
/// </summary>
public sealed class VoiceActivityDetector : IVoiceActivityDetector
{
    private const int TargetSampleRate = 16000;
    private const int FrameMs = 20;
    private const int BytesPerSample = 2;
    private const int FrameBytes = TargetSampleRate / 1000 * FrameMs * BytesPerSample; // 640

    /// <summary>RMS que llena el medidor (voz fuerte típica); Level y Threshold son relativos a esto.</summary>
    private const double FullScaleRms = 4000;

    private readonly object _gate = new();
    private readonly WebRtcVad? _vad;
    private readonly List<byte> _pending = [];
    private bool _isSpeaking;
    private double _level;
    private double _threshold = 400 / FullScaleRms;
    private DateTime _lastSpeechUtc = DateTime.MinValue;

    /// <summary>Cuánto se mantiene "hablando" tras el último frame con voz, para no parpadear pausa/reanuda.</summary>
    public TimeSpan HangoverTime { get; set; } = TimeSpan.FromMilliseconds(500);

    public bool IsSpeaking => _isSpeaking;

    public double Level => Volatile.Read(ref _level);

    public double Threshold
    {
        get => Volatile.Read(ref _threshold);
        set => Volatile.Write(ref _threshold, Math.Clamp(value, 0.001, 1));
    }

    public event Action<bool>? SpeakingChanged;

    public VoiceActivityDetector()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                _vad = new WebRtcVad
                {
                    SampleRate = WebRtcVadSharp.SampleRate.Is16kHz,
                    FrameLength = WebRtcVadSharp.FrameLength.Is20ms,
                    OperatingMode = OperatingMode.HighQuality,
                };
            }
            catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
            {
                // Sin WebRtcVad.dll (o de otra arquitectura, p. ej. win-arm64): solo noise gate.
                BridgeLog.Warn($"WebRTC VAD no disponible, se usa solo noise gate: {ex.Message}");
            }
        }
    }

    /// <summary>Alimenta PCM 16-bit mono a 16kHz; internamente lo trocea en frames de 20ms.</summary>
    public void PushSamples(byte[] pcm)
    {
        bool? changed = null;
        lock (_gate)
        {
            _pending.AddRange(pcm);
            while (_pending.Count >= FrameBytes)
            {
                var frame = _pending.GetRange(0, FrameBytes).ToArray();
                _pending.RemoveRange(0, FrameBytes);
                if (ProcessFrame(frame) is { } state)
                {
                    changed = state;
                }
            }
        }

        if (changed is { } speaking)
        {
            SpeakingChanged?.Invoke(speaking);
        }
    }

    public void Reset()
    {
        var wasSpeaking = false;
        lock (_gate)
        {
            _pending.Clear();
            wasSpeaking = _isSpeaking;
            _isSpeaking = false;
            _lastSpeechUtc = DateTime.MinValue;
            Volatile.Write(ref _level, 0);
        }

        if (wasSpeaking)
        {
            SpeakingChanged?.Invoke(false);
        }
    }

    /// <summary>Devuelve el nuevo estado si cambió en este frame.</summary>
    private bool? ProcessFrame(byte[] frame)
    {
        var rms = ComputeRms(frame);
        var level = Math.Min(1, rms / FullScaleRms);

        // Medidor: sube al instante, baja suave (más legible que el valor crudo de 20ms).
        var shown = Math.Max(level, Volatile.Read(ref _level) * 0.85);
        Volatile.Write(ref _level, shown);

        var hasEnergy = level >= Threshold;
        var hasSpeech = hasEnergy && (_vad?.HasSpeech(frame) ?? true);

        var now = DateTime.UtcNow;
        if (hasSpeech)
        {
            _lastSpeechUtc = now;
        }

        var speaking = now - _lastSpeechUtc < HangoverTime;
        if (speaking == _isSpeaking)
        {
            return null;
        }

        _isSpeaking = speaking;
        return speaking;
    }

    private static double ComputeRms(byte[] frame)
    {
        var sampleCount = frame.Length / BytesPerSample;
        if (sampleCount == 0)
        {
            return 0;
        }

        long sumSquares = 0;
        for (var i = 0; i < sampleCount; i++)
        {
            var sample = BitConverter.ToInt16(frame, i * BytesPerSample);
            sumSquares += (long)sample * sample;
        }

        return Math.Sqrt(sumSquares / (double)sampleCount);
    }

    public void Dispose() => _vad?.Dispose();
}
