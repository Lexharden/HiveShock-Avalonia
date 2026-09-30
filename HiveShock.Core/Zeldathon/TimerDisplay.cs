namespace HiveShock.Zeldathon;

public enum TimerTone
{
    Normal,
    Warn,
    Critical,
    Paused,
    Exhausted,
    Offline,
}

/// <summary>Lo que el overlay del cronómetro debe pintar en este instante.</summary>
public sealed record TimerDisplay(
    string Label,
    string TimeText,
    string StatusText,
    TimerTone Tone,
    string ResetText,
    double UsedFraction);

public sealed class TimerDisplayOptions
{
    public string Label { get; set; } = "Tiempo restante";

    /// <summary>"hms" = 03:59:58, "ms" = 239:58.</summary>
    public string Format { get; set; } = "hms";

    public int WarnMinutes { get; set; } = 30;
    public int CriticalMinutes { get; set; } = 5;

    /// <summary>Presupuesto diario del evento en segundos (para la barra de tiempo usado).</summary>
    public long BudgetSeconds { get; set; } = 4 * 3600;

    public bool LocalCountdown { get; set; }
    public int LocalStartMinutes { get; set; } = 240;
}

/// <summary>Convierte el reloj oficial o el cronómetro local en texto y estado listos para pintar.</summary>
public static class TimerDisplayBuilder
{
    public const string PlaceholderText = "--:--:--";

    public static string FormatTime(long ms, string? format, bool roundUp)
    {
        var total = Math.Max(0, roundUp ? (ms + 999) / 1000 : ms / 1000);
        if (string.Equals(format, "ms", StringComparison.OrdinalIgnoreCase))
        {
            return $"{total / 60:00}:{total % 60:00}";
        }

        return $"{total / 3600:00}:{total % 3600 / 60:00}:{total % 60:00}";
    }

    public static TimerDisplay Official(ZeldathonClock clock, TimerDisplayOptions opt, ZeldathonConnectionState connection)
    {
        var label = string.IsNullOrWhiteSpace(opt.Label) ? "Tiempo restante" : opt.Label.Trim();
        if (!clock.HasValue)
        {
            var status = connection == ZeldathonConnectionState.Connected
                ? "ESPERANDO AL SERVIDOR"
                : connection == ZeldathonConnectionState.AuthFailed
                    ? "TOKEN NO VÁLIDO"
                    : "SIN CONEXIÓN";
            return new TimerDisplay(label, opt.Format == "ms" ? "--:--" : PlaceholderText, status, TimerTone.Offline, "", 0);
        }

        var remaining = clock.RemainingMs();
        var snapshot = clock.Snapshot!;
        var text = FormatTime(remaining, opt.Format, roundUp: true);
        var used = opt.BudgetSeconds > 0
            ? Math.Clamp(1.0 - remaining / (opt.BudgetSeconds * 1000.0), 0, 1)
            : 0;
        var resetText = clock.UntilResetMs() is > 0 and var untilReset
            ? "Reinicia en " + FormatTime(untilReset, "hms", roundUp: false)
            : "";

        if (clock.IsDisconnected)
        {
            return new TimerDisplay(label, text, "SIN CONEXIÓN · RELOJ DETENIDO", TimerTone.Offline, resetText, used);
        }

        var (statusText, tone) = snapshot.Status switch
        {
            ZeldathonRacerStatus.Live => ("EN VIVO", ByThreshold(remaining, opt)),
            ZeldathonRacerStatus.Paused => ("PAUSADO", TimerTone.Paused),
            ZeldathonRacerStatus.Exhausted => ("AGOTADO", TimerTone.Exhausted),
            ZeldathonRacerStatus.Finished => ("TERMINÓ", TimerTone.Normal),
            ZeldathonRacerStatus.Offline => ("SIN CONEXIÓN", TimerTone.Offline),
            _ => ("EN ESPERA", remaining > 0 ? ByThreshold(remaining, opt) : TimerTone.Exhausted),
        };
        return new TimerDisplay(label, text, statusText, tone, resetText, used);
    }

    public static TimerDisplay Local(LocalStopwatch stopwatch, TimerDisplayOptions opt)
    {
        var label = string.IsNullOrWhiteSpace(opt.Label) ? "Cronómetro" : opt.Label.Trim();
        var elapsed = stopwatch.ElapsedMs;
        if (!opt.LocalCountdown)
        {
            var status = stopwatch.IsRunning ? "CORRIENDO" : "PAUSADO";
            return new TimerDisplay(
                label,
                FormatTime(elapsed, opt.Format, roundUp: false),
                status,
                stopwatch.IsRunning ? TimerTone.Normal : TimerTone.Paused,
                "",
                0);
        }

        var total = Math.Max(1, opt.LocalStartMinutes) * 60_000L;
        var remaining = Math.Max(0, total - elapsed);
        var used = Math.Clamp(elapsed / (double)total, 0, 1);
        if (remaining == 0)
        {
            return new TimerDisplay(label, FormatTime(0, opt.Format, true), "TIEMPO", TimerTone.Exhausted, "", 1);
        }

        return new TimerDisplay(
            label,
            FormatTime(remaining, opt.Format, roundUp: true),
            stopwatch.IsRunning ? "CORRIENDO" : "PAUSADO",
            stopwatch.IsRunning ? ByThreshold(remaining, opt) : TimerTone.Paused,
            "",
            used);
    }

    private static TimerTone ByThreshold(long remainingMs, TimerDisplayOptions opt)
    {
        if (remainingMs <= 0)
        {
            return TimerTone.Exhausted;
        }

        if (remainingMs <= Math.Max(0, opt.CriticalMinutes) * 60_000L)
        {
            return TimerTone.Critical;
        }

        return remainingMs <= Math.Max(0, opt.WarnMinutes) * 60_000L ? TimerTone.Warn : TimerTone.Normal;
    }
}
