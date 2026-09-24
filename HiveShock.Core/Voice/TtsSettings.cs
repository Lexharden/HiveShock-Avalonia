using System.Text.Json;
using HiveShock.Configuration;

namespace HiveShock.Voice;

/// <summary>
/// Config de Smart TTS. Vive en Core (no en HiveShock.Avalonia) porque el propio
/// SmartVoiceManager es de vida larga en BridgeRuntime, igual que UiPreferences es
/// para el resto de la UI de escritorio.
/// </summary>
public sealed class TtsSettings
{
    public bool Enabled { get; set; }
    public string VoiceId { get; set; } = "";
    public double Volume { get; set; } = 1.0;
    public int MaxQueueLength { get; set; } = 20;
    public string MuteHotkey { get; set; } = "Control+Alt+M";

    private static string Path => System.IO.Path.Combine(AppPaths.AppDirectory, ".hiveshock-tts.json");

    public static TtsSettings Load()
    {
        try
        {
            if (!File.Exists(Path))
            {
                return new TtsSettings();
            }

            var json = File.ReadAllText(Path);
            return JsonSerializer.Deserialize<TtsSettings>(json) ?? new TtsSettings();
        }
        catch
        {
            return new TtsSettings();
        }
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path, json + Environment.NewLine);
        }
        catch
        {
            // no romper el bridge por un fallo de disco
        }
    }
}
