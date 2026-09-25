using HiveShock.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace HiveShock.Voice;

/// <summary>
/// Captura de micrófono para Windows vía WASAPI (el dispositivo elegido, o el predeterminado).
/// Se pide directamente 16kHz mono 16-bit, que es lo que espera
/// <see cref="VoiceActivityDetector"/> (y WebRTC VAD en general): en modo compartido el
/// motor de audio de Windows (AutoConvertPcm) convierte desde el formato del dispositivo
/// (normalmente 44.1/48kHz estéreo), sin re-muestreo propio ni hilo de bombeo. Vive en
/// HiveShock.Desktop (no en Core) para no arrastrar WASAPI a Android.
/// </summary>
public sealed class WindowsMicrophoneCapture : IMicrophoneCapture
{
    private static readonly WaveFormat TargetFormat = new(16000, 16, 1);

    private readonly object _gate = new();
    private WasapiRecorder? _recorder;
    private MMDevice? _device;

    public int SampleRate => TargetFormat.SampleRate;
    public bool IsCapturing { get; private set; }
    public string DeviceId { get; set; } = "";

    public event Action<byte[]>? SamplesAvailable;

    public void Start()
    {
        lock (_gate)
        {
            if (IsCapturing || !OperatingSystem.IsWindows())
            {
                return;
            }

            var device = ResolveDevice();
            WasapiRecorder? recorder = null;
            try
            {
                recorder = new WasapiRecorderBuilder()
                    .WithDevice(device)
                    .WithSharedMode()
                    .WithFormat(TargetFormat)
                    .Build();

                var format = recorder.WaveFormat;
                if (format.SampleRate != TargetFormat.SampleRate || format.Channels != 1 ||
                    format.BitsPerSample != 16 || format.Encoding != WaveFormatEncoding.Pcm)
                {
                    throw new InvalidOperationException(
                        $"El micrófono entregó {format} en vez de 16kHz mono 16-bit.");
                }

                recorder.DataAvailable += OnDataAvailable;
                recorder.RecordingStopped += OnRecordingStopped;
                recorder.StartRecording();
            }
            catch
            {
                recorder?.Dispose();
                device.Dispose();
                throw;
            }

            _recorder = recorder;
            _device = device;
            IsCapturing = true;
        }
    }

    public IReadOnlyList<AudioDevice> ListDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .Select(d => new AudioDevice(d.ID, d.FriendlyName))
            .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!IsCapturing)
            {
                return;
            }

            if (_recorder != null)
            {
                _recorder.DataAvailable -= OnDataAvailable;
                _recorder.RecordingStopped -= OnRecordingStopped;
                try
                {
                    _recorder.StopRecording();
                }
                catch (Exception ex)
                {
                    BridgeLog.Warn($"Captura de mic al detener: {ex.Message}");
                }

                _recorder.Dispose();
                _recorder = null;
            }

            _device?.Dispose();
            _device = null;
            IsCapturing = false;
        }
    }

    public void Dispose() => Stop();

    /// <summary>
    /// Llega en el hilo de captura de WASAPI. El span solo vale durante la llamada: se copia.
    /// Con la marca Silent el contenido no es fiable y WASAPI pide tratarlo como silencio.
    /// </summary>
    private void OnDataAvailable(ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
    {
        if (buffer.IsEmpty)
        {
            return;
        }

        var samples = flags.HasFlag(AudioClientBufferFlags.Silent) ? new byte[buffer.Length] : buffer.ToArray();
        SamplesAvailable?.Invoke(samples);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            // Típico: se desconectó el micrófono en pleno directo.
            BridgeLog.Warn($"Captura de mic: {e.Exception.Message}");
        }
    }

    /// <summary>El micrófono elegido; si ya no está conectado, el predeterminado de Windows (comunicaciones primero).</summary>
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
                // desconectado o id viejo: cae al predeterminado
            }

            BridgeLog.Warn("Smart TTS: el micrófono elegido no está disponible, se usa el predeterminado.");
        }

        return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
    }
}
