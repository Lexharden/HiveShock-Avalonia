using System.Globalization;
using HiveShock.Voice.Piper.Runtime;
using HiveShock.Voice.Piper.Voices;

namespace HiveShock.Voice.Piper.Synthesis;

/// <summary>
/// <see cref="ISpeechSynthesizer"/> sobre las voces Piper instaladas. Id de voz = clave del
/// catálogo ("es_MX-claude-high"), o "clave#hablante" en modelos con varios hablantes.
/// Velocidad → length_scale (Piper no tiene tono: el motor declara SupportsPitch = false).
/// </summary>
public sealed class PiperSpeechSynthesizer : ISpeechSynthesizer, IDisposable
{
    private readonly PiperRuntime _runtime;
    private readonly PiperVoiceStore _store;
    private readonly PiperPaths _paths;
    private readonly PiperWorkerPool _pool;

    public PiperSpeechSynthesizer(PiperRuntime runtime, PiperVoiceStore store, PiperPaths paths, int maxLoadedVoices = 3)
    {
        _runtime = runtime;
        _store = store;
        _paths = paths;
        _pool = new PiperWorkerPool(maxLoadedVoices);

        // Voz borrada o motor reinstalado: los procesos vivos apuntan a archivos que ya no existen.
        _store.Changed += _pool.Clear;
        _runtime.Changed += _pool.Clear;
    }

    public Task<IReadOnlyList<VoiceProfile>> ListVoicesAsync(CancellationToken ct)
    {
        var voices = new List<VoiceProfile>();
        foreach (var installed in _store.Installed())
        {
            var info = installed.Info;
            var tag = installed.IsCustom ? "voz propia" : info.QualityLabel;
            if (info.NumSpeakers <= 1 || info.Speakers.Count == 0)
            {
                voices.Add(new VoiceProfile(info.Key, $"{info.DisplayName} ({tag})", info.Locale));
                continue;
            }

            foreach (var (speaker, id) in info.Speakers.OrderBy(s => s.Value))
            {
                voices.Add(new VoiceProfile($"{info.Key}#{id}",
                    $"{info.DisplayName} {speaker.Replace('_', ' ')} ({tag})", info.Locale));
            }
        }

        return Task.FromResult<IReadOnlyList<VoiceProfile>>(voices);
    }

    public async Task<Stream> SynthesizeAsync(string text, VoiceProfile voice, SpeechStyle style, CancellationToken ct)
    {
        if (!_runtime.IsInstalled)
        {
            throw new InvalidOperationException("Falta instalar el motor de voces locales (Piper).");
        }

        var (installed, speakerId) = Resolve(voice.Id) ?? throw new InvalidOperationException(
            "No hay voces locales descargadas. Descarga alguna en «Voces locales HD».");

        var worker = _pool.Get(new PiperWorkerOptions(
            _runtime.ExecutablePath,
            _runtime.EspeakDataPath,
            installed.ModelPath,
            installed.ConfigPath,
            LengthScaleFor(style.RatePercent),
            _paths.TempDirectory));
        var audio = await worker.SynthesizeAsync(text, speakerId, ct).ConfigureAwait(false);
        return new MemoryStream(audio, writable: false);
    }

    /// <summary>
    /// Velocidad (-50..+100 %) → length_scale de Piper (duración de cada fonema): +100 % = 0,5;
    /// -50 % = 2,0. Se redondea a pasos de 0,05 para no crear un proceso nuevo por cada
    /// movimiento mínimo del control.
    /// </summary>
    public static double LengthScaleFor(int ratePercent)
    {
        var rate = Math.Clamp(ratePercent, -50, 100);
        var scale = 1.0 / (1.0 + rate / 100.0);
        return Math.Round(scale / 0.05, MidpointRounding.AwayFromZero) * 0.05;
    }

    /// <summary>"clave#3" → (voz, 3). Si la voz pedida no está, la primera en español o la primera instalada.</summary>
    internal (PiperInstalledVoice Voice, int? SpeakerId)? Resolve(string voiceId)
    {
        var installed = _store.Installed();
        if (installed.Count == 0)
        {
            return null;
        }

        var key = voiceId ?? "";
        int? speaker = null;
        var hash = key.LastIndexOf('#');
        if (hash > 0 && int.TryParse(key[(hash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            speaker = id;
            key = key[..hash];
        }

        var voice = installed.FirstOrDefault(v => string.Equals(v.Info.Key, key, StringComparison.OrdinalIgnoreCase));
        if (voice == null)
        {
            voice = installed.FirstOrDefault(v => v.Info.LanguageCode.StartsWith("es", StringComparison.OrdinalIgnoreCase))
                    ?? installed[0];
            speaker = null;
        }

        return (voice, voice.Info.NumSpeakers > 1 ? speaker ?? 0 : null);
    }

    public void Dispose()
    {
        _store.Changed -= _pool.Clear;
        _runtime.Changed -= _pool.Clear;
        _pool.Dispose();
    }
}
