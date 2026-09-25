using Windows.Media.SpeechSynthesis;
using WinRtSynthesizer = Windows.Media.SpeechSynthesis.SpeechSynthesizer;

namespace HiveShock.Voice;

/// <summary>
/// Voces instaladas en Windows (OneCore: Sabina, Raúl, Helena…) vía
/// Windows.Media.SpeechSynthesis. Menos naturales que Edge, pero funcionan sin internet
/// y no dependen de un endpoint no oficial: es la alternativa cuando Microsoft bloquea
/// Edge TTS o no hay conexión. Devuelve WAV; <see cref="WindowsAudioPlayer"/> lo detecta.
/// </summary>
public sealed class WindowsSpeechSynthesizer : ISpeechSynthesizer
{
    public async Task<Stream> SynthesizeAsync(string text, VoiceProfile voice, SpeechStyle style, CancellationToken ct)
    {
        using var synth = new WinRtSynthesizer();
        var chosen = WinRtSynthesizer.AllVoices.FirstOrDefault(v => v.Id == voice.Id)
                     ?? WinRtSynthesizer.AllVoices.FirstOrDefault(v => v.Language.StartsWith("es", StringComparison.OrdinalIgnoreCase))
                     ?? WinRtSynthesizer.DefaultVoice;
        if (chosen == null)
        {
            throw new InvalidOperationException(
                "Windows no tiene voces instaladas. Agrégalas en Configuración > Hora e idioma > Voz.");
        }

        synth.Voice = chosen;
        // SpeakingRate: 0.5..6 (1 = normal). AudioPitch: 0..2 (1 = normal).
        synth.Options.SpeakingRate = Math.Clamp(1 + style.RatePercent / 100.0, 0.5, 3.0);
        synth.Options.AudioPitch = Math.Clamp(1 + style.PitchPercent / 100.0, 0.0, 2.0);

        using var result = await synth.SynthesizeTextToStreamAsync(text).AsTask(ct).ConfigureAwait(false);
        var audio = new MemoryStream();
        await using (var source = result.AsStreamForRead())
        {
            await source.CopyToAsync(audio, ct).ConfigureAwait(false);
        }

        audio.Position = 0;
        return audio;
    }

    public Task<IReadOnlyList<VoiceProfile>> ListVoicesAsync(CancellationToken ct)
    {
        IReadOnlyList<VoiceProfile> voices = WinRtSynthesizer.AllVoices
            .Select(v => new VoiceProfile(v.Id, v.DisplayName.Replace("Microsoft ", "", StringComparison.Ordinal), v.Language))
            .OrderBy(v => v.Locale.StartsWith("es", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(v => v.Locale, StringComparer.OrdinalIgnoreCase)
            .ThenBy(v => v.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Task.FromResult(voices);
    }
}
