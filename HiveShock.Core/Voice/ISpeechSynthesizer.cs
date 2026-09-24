namespace HiveShock.Voice;

/// <summary>Voz disponible para síntesis (id concreto del proveedor + nombre para mostrar).</summary>
public sealed record VoiceProfile(string Id, string DisplayName, string Locale);

/// <summary>
/// Motor de texto a voz. La implementación decide el proveedor (Edge TTS por defecto;
/// Azure Speech u otro más adelante si hace falta) — el resto del sistema solo pide
/// "convierte este texto a audio con esta voz" y no sabe nada del transporte.
/// </summary>
public interface ISpeechSynthesizer
{
    Task<Stream> SynthesizeAsync(string text, VoiceProfile voice, CancellationToken ct);

    Task<IReadOnlyList<VoiceProfile>> ListVoicesAsync(CancellationToken ct);
}
