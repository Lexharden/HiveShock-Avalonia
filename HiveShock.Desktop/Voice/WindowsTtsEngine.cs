namespace HiveShock.Voice;

/// <summary>Voces instaladas en Windows (OneCore): sin internet, siempre disponibles, más robóticas.</summary>
public sealed class WindowsTtsEngine(ISpeechSynthesizer synthesizer) : TtsEngineBase
{
    public WindowsTtsEngine() : this(new WindowsSpeechSynthesizer())
    {
    }

    public override string Id => TtsEngines.Windows;
    public override string DisplayName => "Voces de Windows";
    public override string Description => "Funcionan sin internet. Suenan más robóticas.";
    public override bool RequiresInternet => false;
    public override string ReadyText => "Voces de Windows listas";

    public override string NoVoicesMessage =>
        "Windows no tiene voces instaladas. Agrégalas en Configuración > Hora e idioma > Voz.";

    public override ISpeechSynthesizer Synthesizer { get; } = synthesizer;

    /// <summary>Listar basta (sintetizar sería más lento y no añade nada): sin voces no hay motor.</summary>
    public override async Task CheckAsync(string voiceId, CancellationToken ct)
    {
        var voices = await Synthesizer.ListVoicesAsync(ct).ConfigureAwait(false);
        if (voices.Count == 0)
        {
            throw new InvalidOperationException(NoVoicesMessage);
        }
    }
}
