using HiveShock.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace HiveShock.Voice;

/// <summary>
/// Reproduce lo que devuelve <see cref="ISpeechSynthesizer"/> por WASAPI en la salida
/// elegida (útil para mandar la voz a un cable virtual que capture OBS). Acepta MP3
/// (Edge TTS: audio-24khz-48kbitrate-mono-mp3) y WAV (voces de Windows). WASAPI en modo
/// compartido no re-muestrea, así que se convierte al sample rate del dispositivo aquí.
/// PlayAsync espera a que termine o a que se cancele el token — <see cref="SmartVoiceManager"/>
/// cancela a mitad de reproducción al saltar, al silenciar o cuando el streamer habla.
/// </summary>
public sealed class WindowsAudioPlayer : IAudioPlayer
{
    private readonly object _gate = new();
    private IWavePlayer? _output;
    private VolumeSampleProvider? _volumeProvider;
    private double _volume = 1.0;

    public bool IsPlaying { get; private set; }
    public string DeviceId { get; set; } = "";

    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 1);
            lock (_gate)
            {
                if (_volumeProvider != null)
                {
                    _volumeProvider.Volume = (float)_volume;
                }
            }
        }
    }

    public IReadOnlyList<AudioDevice> ListDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .Select(d => new AudioDevice(d.ID, d.FriendlyName))
            .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task PlayAsync(Stream audio, CancellationToken ct)
    {
        Stop();

        var buffered = new MemoryStream();
        await audio.CopyToAsync(buffered, ct).ConfigureAwait(false);
        buffered.Position = 0;

        using var reader = OpenReader(buffered);
        using var device = ResolveDevice();
        int mixRate;
        using (var client = device.CreateAudioClient())
        {
            mixRate = client.MixFormat.SampleRate;
        }

        var volume = new VolumeSampleProvider(reader.ToSampleProvider()) { Volume = (float)_volume };
        ISampleProvider chain = volume;
        if (chain.WaveFormat.SampleRate != mixRate)
        {
            chain = new WdlResamplingSampleProvider(chain, mixRate);
        }

#pragma warning disable CS0618 // WasapiOut: el reemplazo (WasapiPlayerBuilder) aún no expone PlaybackStopped igual
        using var output = new WasapiOut(device, AudioClientShareMode.Shared, true, 100);
#pragma warning restore CS0618
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
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
        lock (_gate)
        {
            _output = output;
            _volumeProvider = volume;
        }

        try
        {
            IsPlaying = true;
            output.Init(chain);
            output.Play();

            using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
            await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            output.PlaybackStopped -= OnStopped;
            try
            {
                output.Stop();
            }
            catch
            {
                // ya detenido
            }

            lock (_gate)
            {
                if (ReferenceEquals(_output, output))
                {
                    _output = null;
                    _volumeProvider = null;
                }
            }

            IsPlaying = false;
        }
    }

    public void Stop()
    {
        IWavePlayer? output;
        lock (_gate)
        {
            output = _output;
        }

        try
        {
            output?.Stop();
        }
        catch
        {
            // el reproductor ya pudo haberse liberado
        }
    }

    public void Dispose() => Stop();

    /// <summary>WAV empieza por "RIFF"; todo lo demás se trata como MP3.</summary>
    private static WaveStream OpenReader(MemoryStream audio)
    {
        var head = audio.GetBuffer();
        if (audio.Length >= 4 && head[0] == 'R' && head[1] == 'I' && head[2] == 'F' && head[3] == 'F')
        {
            return new WaveFileReader(audio);
        }

        return new Mp3FileReader(audio);
    }

    /// <summary>La salida elegida; si ya no está conectada, la predeterminada de Windows.</summary>
    private MMDevice ResolveDevice()
    {
        using var enumerator = new MMDeviceEnumerator();
        if (!string.IsNullOrWhiteSpace(DeviceId))
        {
            try
            {
                var device = enumerator.GetDevice(DeviceId);
                if (device.State == DeviceState.Active)
                {
                    return device;
                }
            }
            catch
            {
                // desconectada o id viejo: cae a la predeterminada
            }

            BridgeLog.Warn("Smart TTS: la salida de audio elegida no está disponible, se usa la predeterminada.");
        }

        return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    }
}
