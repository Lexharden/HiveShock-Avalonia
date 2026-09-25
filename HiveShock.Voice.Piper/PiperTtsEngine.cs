using HiveShock.Voice.Piper.Runtime;
using HiveShock.Voice.Piper.Synthesis;
using HiveShock.Voice.Piper.Voices;

namespace HiveShock.Voice.Piper;

/// <summary>
/// Voces neurales locales con Piper: naturales, sin internet y sin depender de ningún servicio.
/// Solo está disponible tras instalar el motor y al menos una voz (lo hace la UI con
/// <see cref="Runtime"/>, <see cref="Catalog"/> y <see cref="Store"/>).
/// </summary>
public sealed class PiperTtsEngine : TtsEngineBase, IDisposable
{
    private readonly PiperSpeechSynthesizer _synthesizer;

    public PiperTtsEngine(PiperPaths? paths = null)
    {
        Paths = paths ?? PiperPaths.Default;
        Runtime = new PiperRuntime(Paths);
        Catalog = new PiperVoiceCatalog(Paths);
        Store = new PiperVoiceStore(Paths);
        _synthesizer = new PiperSpeechSynthesizer(Runtime, Store, Paths);
        CleanTemp();
    }

    public PiperPaths Paths { get; }
    public PiperRuntime Runtime { get; }
    public PiperVoiceCatalog Catalog { get; }
    public PiperVoiceStore Store { get; }

    public override string Id => TtsEngines.Piper;
    public override string DisplayName => "Voces locales HD (Piper)";
    public override string Description => "Naturales y sin internet. Se descargan una sola vez.";
    public override bool RequiresInternet => false;
    public override bool SupportsPitch => false;
    public override bool IsAvailable => Runtime.IsInstalled && Store.Installed().Count > 0;

    public override string UnavailableReason =>
        !Runtime.IsSupported ? "Las voces locales HD no están disponibles para este sistema."
        : !Runtime.IsInstalled ? "Falta instalar el motor de voces locales HD."
        : "Descarga al menos una voz local HD.";

    public override string ReadyText => "Voces locales listas";

    public override string NoVoicesMessage =>
        "No hay voces locales descargadas. Descarga alguna en «Voces locales HD».";

    public override ISpeechSynthesizer Synthesizer => _synthesizer;

    /// <summary>Espacio que ocupan motor + voces, para mostrarlo al streamer.</summary>
    public long DiskUsageBytes()
    {
        if (!Directory.Exists(Paths.Root))
        {
            return 0;
        }

        try
        {
            return new DirectoryInfo(Paths.Root).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        }
        catch (IOException)
        {
            return 0;
        }
    }

    /// <summary>Borra motor, voces y catálogo: deja todo como antes de instalar.</summary>
    public void UninstallAll()
    {
        Store.DeleteAll();
        Runtime.Uninstall();
        CleanTemp();
    }

    private void CleanTemp()
    {
        try
        {
            if (Directory.Exists(Paths.TempDirectory))
            {
                Directory.Delete(Paths.TempDirectory, recursive: true);
            }
        }
        catch
        {
            // algún WAV en uso: se limpia la próxima vez
        }
    }

    public void Dispose() => _synthesizer.Dispose();
}
