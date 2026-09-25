namespace HiveShock.Voice;

/// <summary>Voz disponible para síntesis (id concreto del proveedor + nombre para mostrar).</summary>
public sealed record VoiceProfile(string Id, string DisplayName, string Locale)
{
    /// <summary>Texto para el desplegable: "Dalia (es-MX)".</summary>
    public string Label => string.IsNullOrWhiteSpace(Locale) ? DisplayName : $"{DisplayName} ({Locale})";
}

/// <summary>
/// Velocidad y tono relativos a la voz original. RatePercent: -50..+100 (0 = normal).
/// PitchPercent: -50..+50 (0 = normal). Cada motor lo traduce a su propia escala.
/// </summary>
public sealed record SpeechStyle(int RatePercent, int PitchPercent)
{
    public static readonly SpeechStyle Normal = new(0, 0);
}

/// <summary>
/// Motor de texto a voz. La implementación decide el proveedor (Edge TTS en línea, voces
/// de Windows sin internet, …) — el resto del sistema solo pide "convierte este texto a
/// audio con esta voz" y no sabe nada del transporte. El Stream devuelto es MP3 o WAV;
/// <see cref="IAudioPlayer"/> detecta el formato.
/// </summary>
public interface ISpeechSynthesizer
{
    Task<Stream> SynthesizeAsync(string text, VoiceProfile voice, SpeechStyle style, CancellationToken ct);

    Task<IReadOnlyList<VoiceProfile>> ListVoicesAsync(CancellationToken ct);
}

/// <summary>
/// El proveedor rechazó la conexión a propósito (HTTP 403): no es un corte de internet
/// sino un bloqueo del servicio. <see cref="SmartVoiceManager"/> lo trata distinto a un
/// error pasajero: detiene la lectura y la UI bloquea la sección con un aviso.
/// </summary>
public sealed class SpeechServiceBlockedException(string message) : Exception(message);
