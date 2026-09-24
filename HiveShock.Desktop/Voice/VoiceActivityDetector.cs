using WebRtcVadSharp;

namespace HiveShock.Voice;

/// <summary>
/// Implementación de <see cref="IVoiceActivityDetector"/> para escritorio. Dos etapas:
/// 1) un noise gate barato (energía RMS) que corre siempre, para no gastar CPU en
///    silencio ni molestar al VAD con ruido de fondo constante;
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

    private readonly WebRtcVad? _vad;
    private readonly List<byte> _pending = [];
    private bool _isSpeaking;
    private DateTime _lastSpeechUtc = DateTime.MinValue;

    /// <summary>RMS mínimo (0..32767) para considerar que hay algo más que ruido de fondo.</summary>
    public short NoiseGateThreshold { get; set; } = 400;

    /// <summary>Cuánto se mantiene "hablando" tras el último frame con voz, para no parpadear pausa/reanuda.</summary>
    public TimeSpan HangoverTime { get; set; } = TimeSpan.FromMilliseconds(400);

    public bool IsSpeaking => _isSpeaking;

    public event Action<bool>? SpeakingChanged;

    public VoiceActivityDetector()
    {
        if (OperatingSystem.IsWindows())
        {
            _vad = new WebRtcVad
            {
                SampleRate = WebRtcVadSharp.SampleRate.Is16kHz,
                FrameLength = WebRtcVadSharp.FrameLength.Is20ms,
                OperatingMode = OperatingMode.HighQuality,
            };
        }
    }

    /// <summary>Alimenta PCM 16-bit mono a 16kHz; internamente lo trocea en frames de 20ms.</summary>
    public void PushSamples(byte[] pcm)
    {
        _pending.AddRange(pcm);
        while (_pending.Count >= FrameBytes)
        {
            var frame = _pending.GetRange(0, FrameBytes).ToArray();
            _pending.RemoveRange(0, FrameBytes);
            ProcessFrame(frame);
        }
    }

    public void Reset()
    {
        _pending.Clear();
        if (_isSpeaking)
        {
            _isSpeaking = false;
            SpeakingChanged?.Invoke(false);
        }

        _lastSpeechUtc = DateTime.MinValue;
    }

    private void ProcessFrame(byte[] frame)
    {
        var hasEnergy = ComputeRms(frame) >= NoiseGateThreshold;
        var hasSpeech = hasEnergy && (_vad?.HasSpeech(frame) ?? true);

        var now = DateTime.UtcNow;
        if (hasSpeech)
        {
            _lastSpeechUtc = now;
        }

        var speaking = now - _lastSpeechUtc < HangoverTime;
        if (speaking != _isSpeaking)
        {
            _isSpeaking = speaking;
            SpeakingChanged?.Invoke(speaking);
        }
    }

    private static short ComputeRms(byte[] frame)
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

        return (short)Math.Sqrt(sumSquares / (double)sampleCount);
    }

    public void Dispose() => _vad?.Dispose();
}
