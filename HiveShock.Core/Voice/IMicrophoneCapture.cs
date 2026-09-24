namespace HiveShock.Voice;

/// <summary>
/// Fuente de audio del micrófono en crudo (PCM 16-bit mono). La implementación decide
/// el backend (WASAPI en Windows, luego PortAudio/CoreAudio en Mac/Linux) y expone su
/// propio sample rate — <see cref="VoiceActivityDetector"/> se adapta a lo que reciba.
/// </summary>
public interface IMicrophoneCapture : IDisposable
{
    int SampleRate { get; }
    bool IsCapturing { get; }

    /// <summary>Frames PCM 16-bit mono según llegan del dispositivo. No asumir un tamaño fijo de buffer.</summary>
    event Action<byte[]>? SamplesAvailable;

    void Start();
    void Stop();
}
