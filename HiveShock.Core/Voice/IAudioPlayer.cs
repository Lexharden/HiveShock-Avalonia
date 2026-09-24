namespace HiveShock.Voice;

/// <summary>
/// Reproduce el audio que devuelve <see cref="ISpeechSynthesizer"/>. La implementación
/// decide el backend (WaveOut/WASAPI en Windows); PlayAsync espera a que termine o a
/// que se cancele — así <see cref="SmartVoiceManager"/> puede cortar en seco al mutear.
/// </summary>
public interface IAudioPlayer : IDisposable
{
    bool IsPlaying { get; }

    /// <summary>0..1. La implementación la aplica de inmediato, incluso a mitad de reproducción.</summary>
    double Volume { get; set; }

    Task PlayAsync(Stream audio, CancellationToken ct);

    void Stop();
}
