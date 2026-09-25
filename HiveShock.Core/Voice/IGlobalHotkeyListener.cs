namespace HiveShock.Voice;

/// <summary>
/// Atajos globales a nivel de sistema operativo (funcionan aunque HiveShock no tenga el
/// foco). Los combos se pasan como texto ("Control+Alt+M") para no filtrar el tipo de
/// tecla de la librería concreta (hoy SharpHook) hacia el resto del sistema.
/// </summary>
public interface IGlobalHotkeyListener : IDisposable
{
    bool IsListening { get; }

    /// <summary>Se dispara con el combo de silenciar. No indica mute/unmute, solo "toggle".</summary>
    event Action? MuteToggleRequested;

    /// <summary>Se dispara con el combo de saltar el mensaje que se está leyendo.</summary>
    event Action? SkipRequested;

    /// <summary>True si el texto se entiende como combinación de teclas ("Control+Alt+M").</summary>
    bool IsValidCombo(string comboText);

    /// <summary>Arranca (o reaplica) los atajos. Un combo vacío desactiva esa acción.</summary>
    void Start(string muteCombo, string skipCombo);

    void Stop();
}
