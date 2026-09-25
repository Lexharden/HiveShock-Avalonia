namespace HiveShock.Voice;

/// <summary>Dispositivo de audio del sistema (micrófono o salida). Id vacío = el predeterminado de Windows.</summary>
public sealed record AudioDevice(string Id, string Name);

/// <summary>
/// Reproduce el audio que devuelve <see cref="ISpeechSynthesizer"/> (MP3 o WAV). La
/// implementación decide el backend (WASAPI en Windows); PlayAsync espera a que termine
/// o a que se cancele — así <see cref="SmartVoiceManager"/> puede cortar en seco al mutear.
/// </summary>
public interface IAudioPlayer : IDisposable
{
    bool IsPlaying { get; }

    /// <summary>0..1. La implementación la aplica de inmediato, incluso a mitad de reproducción.</summary>
    double Volume { get; set; }

    /// <summary>Salida a usar desde la próxima reproducción. Vacío = predeterminada.</summary>
    string DeviceId { get; set; }

    IReadOnlyList<AudioDevice> ListDevices();

    Task PlayAsync(Stream audio, CancellationToken ct);

    void Stop();
}
