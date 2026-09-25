namespace HiveShock.Voice;

/// <summary>
/// Fuente de audio del micrófono en crudo (PCM 16-bit mono). La implementación decide
/// el backend (WASAPI en Windows, luego PortAudio/CoreAudio en Mac/Linux) y expone su
/// propio sample rate — <see cref="IVoiceActivityDetector"/> se adapta a lo que reciba.
/// </summary>
public interface IMicrophoneCapture : IDisposable
{
    int SampleRate { get; }
    bool IsCapturing { get; }

    /// <summary>Micrófono a usar en el próximo Start. Vacío = predeterminado.</summary>
    string DeviceId { get; set; }

    /// <summary>Frames PCM 16-bit mono según llegan del dispositivo. No asumir un tamaño fijo de buffer.</summary>
    event Action<byte[]>? SamplesAvailable;

    IReadOnlyList<AudioDevice> ListDevices();

    void Start();
    void Stop();
}
