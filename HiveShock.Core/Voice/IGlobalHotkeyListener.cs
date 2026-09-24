namespace HiveShock.Voice;

/// <summary>
/// Atajo global a nivel de sistema operativo (funciona aunque HiveShock no tenga el
/// foco). El combo se pasa como texto ("Control+Alt+M") para no filtrar el tipo de
/// tecla de la librería concreta (hoy SharpHook) hacia el resto del sistema.
/// </summary>
public interface IGlobalHotkeyListener : IDisposable
{
    bool IsListening { get; }

    /// <summary>Se dispara cuando el usuario presiona el combo activo. No indica mute/unmute, solo "toggle".</summary>
    event Action? MuteToggleRequested;

    void Start(string comboText);

    void Stop();
}
