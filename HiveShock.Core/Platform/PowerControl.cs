using HiveShock.Platform.Windows;

namespace HiveShock.Platform;

/// <summary>Acciones de energía del equipo donde corre HiveShock. Hoy solo reiniciar, solo Windows.</summary>
public interface IPowerControl
{
    /// <summary>Si este sistema puede reiniciarse desde HiveShock.</summary>
    bool IsSupported { get; }

    /// <summary>
    /// Pide el reinicio inmediato. Con <paramref name="force"/> se cierran los programas sin preguntar
    /// (se pierde lo que no esté guardado). Lanza si el sistema lo rechaza.
    /// </summary>
    void Restart(bool force);
}

public sealed class NullPowerControl : IPowerControl
{
    public bool IsSupported => false;

    public void Restart(bool force) =>
        throw new PlatformNotSupportedException("Reiniciar el equipo desde HiveShock solo está disponible en Windows.");
}

public static class PowerControlFactory
{
    public static IPowerControl Create() =>
        OperatingSystem.IsWindows() ? new Win32PowerControl() : new NullPowerControl();
}
