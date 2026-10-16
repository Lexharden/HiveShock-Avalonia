namespace HiveShock.Input;

public enum KeyAction
{
    /// <summary>Pulsa y suelta (tiempo fijo corto).</summary>
    Tap,

    /// <summary>Pulsa, mantiene <see cref="KeyStep.HoldMs"/> y suelta.</summary>
    Hold,

    /// <summary>Pulsa y deja pulsada (se suelta con un <see cref="Up"/> o al terminar la macro).</summary>
    Down,

    /// <summary>Suelta una tecla dejada con <see cref="Down"/>.</summary>
    Up,
}

/// <summary>Qué hacer si la misma macro se activa mientras ya está en cola o ejecutándose.</summary>
public enum MacroOverlap
{
    Queue,
    Drop,
    Replace,
}

public sealed record KeyStep
{
    /// <summary>Nombre de la tecla (<see cref="GameKeys"/>): W, Space, Ctrl, F5, Left, Num5…</summary>
    public string Key { get; init; } = "";

    public KeyAction Action { get; init; } = KeyAction.Tap;

    /// <summary>Solo para <see cref="KeyAction.Hold"/>.</summary>
    public int HoldMs { get; init; } = 500;

    public int DelayAfterMs { get; init; }

    public int Repeat { get; init; } = 1;

    /// <summary>Variación aleatoria ±% de los tiempos, para que no parezca robótico.</summary>
    public int JitterPercent { get; init; }
}

public sealed record KeyMacro
{
    public string Id { get; init; } = "";

    public string Label { get; init; } = "";

    public List<KeyStep> Steps { get; init; } = [];

    public MacroOverlap Overlap { get; init; } = MacroOverlap.Queue;

    /// <summary>Tope de duración total; protege contra macros que bloqueen el teclado.</summary>
    public int MaxTotalMs { get; init; } = 10_000;

    /// <summary>Permite combinaciones bloqueadas por defecto (Win, Alt+F4, Alt+Tab…).</summary>
    public bool AllowDangerous { get; init; }

    /// <summary>Atajo global opcional para lanzarla a mano (p. ej. «Ctrl+Alt+F5»). Necesita al menos un modificador.</summary>
    public string? Hotkey { get; init; }
}

/// <summary>Cómo localizar la ventana del juego. Se guarda por perfil.</summary>
public sealed record WindowTarget
{
    public string? ProcessName { get; init; }

    public string? TitleContains { get; init; }

    public string? TitleRegex { get; init; }

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(ProcessName) &&
        string.IsNullOrWhiteSpace(TitleContains) &&
        string.IsNullOrWhiteSpace(TitleRegex);
}

public enum WindowStatus
{
    Ready,
    Minimized,
    NotFound,

    /// <summary>El juego corre elevado y HiveShock no: Windows (UIPI) bloquea las teclas.</summary>
    Elevated,

    /// <summary>Plataforma sin soporte de teclas.</summary>
    Unsupported,
}
