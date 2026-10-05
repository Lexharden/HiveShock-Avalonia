namespace HiveShock.Avalonia.Services;

/// <summary>
/// Ajustes propios del cronómetro en pantalla. Colores, tamaño de título, fondo, escala y alineación se
/// comparten con los demás overlays (<see cref="OverlayLook"/>); aquí va solo lo específico.
/// </summary>
public sealed class TimerPreferences
{
    public bool Enabled { get; set; }
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;

    /// <summary>"official" = reloj del servidor de Zeldatón; "local" = cronómetro manual.</summary>
    public string Mode { get; set; } = "official";

    public string Label { get; set; } = "Tiempo restante";

    /// <summary>"hms" (03:59:58) o "ms" (239:58).</summary>
    public string Format { get; set; } = "hms";

    public double TimeSize { get; set; } = 60;
    public bool ShowStatus { get; set; } = true;
    public bool ShowReset { get; set; }
    public bool ShowBar { get; set; } = true;

    public int WarnMinutes { get; set; } = 30;
    public int CriticalMinutes { get; set; } = 5;

    /// <summary>Colores por estado (#RRGGBB). Vacío = el del tema / el de los demás overlays.</summary>
    public string NormalColor { get; set; } = "";
    public string WarnColor { get; set; } = "#EBA00A";
    public string CriticalColor { get; set; } = "#E07A7A";
    public string ExhaustedColor { get; set; } = "#E07A7A";
    public string PausedColor { get; set; } = "";
    public string OfflineColor { get; set; } = "";

    /// <summary>Rótulo animado «+1:30» / «−0:30» bajo el reloj cuando una donación cambia el tiempo (solo reloj oficial).</summary>
    public bool ShowDelta { get; set; } = true;

    public string DeltaAddColor { get; set; } = "#5BD68A";
    public string DeltaRemoveColor { get; set; } = "#E07A7A";

    /// <summary>Cronómetro local: sube desde cero (false) o baja desde <see cref="LocalStartMinutes"/> (true).</summary>
    public bool LocalCountdown { get; set; }
    public int LocalStartMinutes { get; set; } = 240;

    /// <summary>Tiempo acumulado del cronómetro local al cerrar (se restaura en pausa).</summary>
    public long LocalElapsedMs { get; set; }

    /// <summary>Lo que las donaciones sumaron o restaron al cronómetro manual (se restaura al volver a abrir).</summary>
    public long LocalOffsetMs { get; set; }

    public bool IsLocal => string.Equals(Mode, "local", StringComparison.OrdinalIgnoreCase);

    public double ResolveTimeSize() =>
        double.IsNaN(TimeSize) || TimeSize <= 0 ? 60 : Math.Clamp(TimeSize, 28, 140);

    public void ResetLook()
    {
        Label = "Tiempo restante";
        Format = "hms";
        TimeSize = 60;
        ShowStatus = true;
        ShowReset = false;
        ShowBar = true;
        WarnMinutes = 30;
        CriticalMinutes = 5;
        NormalColor = "";
        WarnColor = "#EBA00A";
        CriticalColor = "#E07A7A";
        ExhaustedColor = "#E07A7A";
        PausedColor = "";
        OfflineColor = "";
        ShowDelta = true;
        DeltaAddColor = "#5BD68A";
        DeltaRemoveColor = "#E07A7A";
    }
}
