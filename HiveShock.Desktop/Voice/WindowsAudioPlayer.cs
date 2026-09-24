using NAudio.Wave;

namespace HiveShock.Voice;

/// <summary>
/// Reproduce el MP3 que devuelve <see cref="ISpeechSynthesizer"/> (Edge TTS entrega
/// audio-24khz-48kbitrate-mono-mp3) usando WaveOut. PlayAsync espera a que termine
/// o a que se cancele el token — <see cref="SmartVoiceManager"/> cancela a mitad de
/// reproducción cuando el streamer empieza a hablar o se activa el mute.
/// </summary>
public sealed class WindowsAudioPlayer : IAudioPlayer
{
    private WaveOut? _output;
    private double _volume = 1.0;

    public bool IsPlaying { get; private set; }

    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 1);
            if (_output != null)
            {
                _output.Volume = (float)_volume;
            }
        }
    }

    public async Task PlayAsync(Stream audio, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Stop();

        var buffered = new MemoryStream();
        await audio.CopyToAsync(buffered, ct).ConfigureAwait(false);
        buffered.Position = 0;

        using var mp3 = new Mp3FileReader(buffered);
        using var output = new WaveOut { Volume = (float)_volume };
        _output = output;

        var tcs = new TaskCompletionSource();
        void OnStopped(object? _, StoppedEventArgs e)
        {
            if (e.Exception != null)
            {
                tcs.TrySetException(e.Exception);
            }
            else
            {
                tcs.TrySetResult();
            }
        }

        output.PlaybackStopped += OnStopped;
        try
        {
            IsPlaying = true;
            output.Init(mp3);
            output.Play();

            using var reg = ct.Register(() => output.Stop());
            await tcs.Task.ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
        }
        finally
        {
            output.PlaybackStopped -= OnStopped;
            IsPlaying = false;
            _output = null;
        }
    }

    public void Stop()
    {
        try
        {
            _output?.Stop();
        }
        catch
        {
            // el reproductor ya pudo haberse liberado
        }
    }

    public void Dispose() => Stop();
}
