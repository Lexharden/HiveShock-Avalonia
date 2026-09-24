using HiveShock.Logging;
using NAudio.CoreAudioApi;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace HiveShock.Voice;

/// <summary>
/// Captura de micrófono para Windows vía WASAPI (dispositivo de grabación por defecto).
/// El mix format del dispositivo suele venir a 44.1/48kHz estéreo; se re-muestrea a
/// 16kHz mono 16-bit con Media Foundation porque es lo que espera
/// <see cref="VoiceActivityDetector"/> (y WebRTC VAD en general). Vive en HiveShock.Desktop
/// (no en Core) para no arrastrar WASAPI/Media Foundation a Android.
/// </summary>
public sealed class WindowsMicrophoneCapture : IMicrophoneCapture
{
    private static readonly WaveFormat TargetFormat = new(16000, 16, 1);
    private const int ReadChunkBytes = 3200; // 100ms a 16kHz/16-bit mono

    private WasapiCapture? _capture;
    private MediaFoundationResampler? _resampler;
    private CancellationTokenSource? _pumpCts;
    private Task? _pumpTask;

    public int SampleRate => TargetFormat.SampleRate;
    public bool IsCapturing { get; private set; }

    public event Action<byte[]>? SamplesAvailable;

    public void Start()
    {
        if (IsCapturing || !OperatingSystem.IsWindows())
        {
            return;
        }

        MediaFoundationApi.Startup();

        var capture = new WasapiCapture();
        var buffer = new BufferedWaveProvider(capture.WaveFormat, TimeSpan.FromSeconds(5))
        {
            DiscardOnBufferOverflow = true,
        };
        var resampler = new MediaFoundationResampler(buffer, TargetFormat) { ResamplerQuality = 60 };

        capture.DataAvailable += (_, e) => buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
        capture.RecordingStopped += (_, e) =>
        {
            if (e.Exception != null)
            {
                BridgeLog.Warn($"Captura de mic: {e.Exception.Message}");
            }
        };

        _capture = capture;
        _resampler = resampler;
        capture.StartRecording();

        _pumpCts = new CancellationTokenSource();
        var token = _pumpCts.Token;
        _pumpTask = Task.Run(() => PumpLoop(resampler, token), token);
        IsCapturing = true;
    }

    private void PumpLoop(MediaFoundationResampler resampler, CancellationToken ct)
    {
        var chunk = new byte[ReadChunkBytes];
        while (!ct.IsCancellationRequested)
        {
            int read;
            try
            {
                read = resampler.Read(chunk);
            }
            catch (Exception ex)
            {
                BridgeLog.Warn($"Captura de mic: {ex.Message}");
                return;
            }

            if (read > 0)
            {
                var samples = new byte[read];
                Buffer.BlockCopy(chunk, 0, samples, 0, read);
                SamplesAvailable?.Invoke(samples);
            }
            else
            {
                Thread.Sleep(10);
            }
        }
    }

    public void Stop()
    {
        if (!IsCapturing)
        {
            return;
        }

        _pumpCts?.Cancel();
        try
        {
            _pumpTask?.Wait(500);
        }
        catch
        {
            // best effort: no bloquear el apagado por el hilo de bombeo
        }

        _pumpCts?.Dispose();
        _pumpCts = null;
        _pumpTask = null;

        _capture?.StopRecording();
        _capture?.Dispose();
        _capture = null;

        _resampler?.Dispose();
        _resampler = null;

        IsCapturing = false;
    }

    public void Dispose() => Stop();
}
