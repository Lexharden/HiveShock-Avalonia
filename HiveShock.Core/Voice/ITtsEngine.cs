namespace HiveShock.Voice;

/// <summary>
/// Un motor de voz completo (Edge, voces de Windows, Piper…): su sintetizador más lo que la
/// UI y <see cref="SmartVoiceManager"/> necesitan saber de él. Añadir un motor nuevo es
/// implementar esta interfaz y registrarlo en <see cref="TtsEngineRegistry"/>; la cola, los
/// filtros y la voz aleatoria no cambian.
/// </summary>
public interface ITtsEngine
{
    /// <summary>Id estable que se guarda en la config (<see cref="TtsSettings.Engine"/>).</summary>
    string Id { get; }

    /// <summary>Nombre para el streamer: "Voces de Microsoft Edge".</summary>
    string DisplayName { get; }

    /// <summary>Una línea sin tecnicismos: "Las más naturales. Necesitan internet."</summary>
    string Description { get; }

    /// <summary>True si depende de un servicio en línea (puede quedarse sin conexión o bloquearse).</summary>
    bool RequiresInternet { get; }

    /// <summary>False si el motor no sabe cambiar el tono: la UI desactiva ese control.</summary>
    bool SupportsPitch { get; }

    /// <summary>False si todavía no se puede usar (p. ej. falta instalarlo); ver <see cref="UnavailableReason"/>.</summary>
    bool IsAvailable { get; }

    string UnavailableReason { get; }

    /// <summary>Estado "listo" para la cabecera de la página: "Voces de Windows listas".</summary>
    string ReadyText { get; }

    /// <summary>Qué decirle al streamer si el motor no tiene ninguna voz.</summary>
    string NoVoicesMessage { get; }

    ISpeechSynthesizer Synthesizer { get; }

    /// <summary>
    /// Comprueba que el motor funciona de verdad (sin reproducir nada). Lanza
    /// <see cref="SpeechServiceBlockedException"/> si el proveedor bloqueó el acceso, u otra
    /// excepción con un mensaje para el streamer si falla.
    /// </summary>
    Task CheckAsync(string voiceId, CancellationToken ct);
}

/// <summary>Base común: disponible siempre y comprobación sintetizando una frase corta.</summary>
public abstract class TtsEngineBase : ITtsEngine
{
    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract string Description { get; }
    public abstract bool RequiresInternet { get; }
    public virtual bool SupportsPitch => true;
    public virtual bool IsAvailable => true;
    public virtual string UnavailableReason => "";
    public virtual string ReadyText => $"{DisplayName} listas";
    public virtual string NoVoicesMessage => "No llegó ninguna voz. Pulsa Actualizar para intentarlo de nuevo.";
    public abstract ISpeechSynthesizer Synthesizer { get; }

    public virtual async Task CheckAsync(string voiceId, CancellationToken ct)
    {
        var probe = await Synthesizer
            .SynthesizeAsync("Hola", new VoiceProfile(voiceId, voiceId, ""), SpeechStyle.Normal, ct)
            .ConfigureAwait(false);
        await probe.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>Voces neurales de Microsoft Edge (en línea, endpoint no oficial).</summary>
public sealed class EdgeTtsEngine(ISpeechSynthesizer synthesizer) : TtsEngineBase
{
    public EdgeTtsEngine() : this(new EdgeTtsSpeechSynthesizer())
    {
    }

    public override string Id => TtsEngines.Edge;
    public override string DisplayName => "Voces de Microsoft Edge";
    public override string Description => "Las más naturales. Necesitan internet.";
    public override bool RequiresInternet => true;
    public override string ReadyText => "Servicio de voces conectado";
    public override ISpeechSynthesizer Synthesizer { get; } = synthesizer;
}
