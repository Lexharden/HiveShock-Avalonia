namespace HiveShock.Voice;

/// <summary>
/// Decide si el streamer está hablando, para pausar Smart TTS. La implementación decide
/// la técnica (noise gate, WebRTC VAD, …) — <see cref="SmartVoiceManager"/> solo necesita
/// que le empujen PCM y le avisen cuando cambia el estado de "hablando".
/// </summary>
public interface IVoiceActivityDetector : IDisposable
{
    bool IsSpeaking { get; }

    event Action<bool>? SpeakingChanged;

    /// <summary>Alimenta PCM 16-bit mono a 16kHz (lo que entrega <see cref="IMicrophoneCapture"/>).</summary>
    void PushSamples(byte[] pcm);

    void Reset();
}
