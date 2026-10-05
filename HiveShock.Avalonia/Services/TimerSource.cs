using Avalonia.Media;
using HiveShock.Avalonia.Themes;
using HiveShock.Zeldathon;

namespace HiveShock.Avalonia.Services;

/// <summary>
/// Da al overlay lo que tiene que pintar en cada instante: el reloj oficial de Zeldatón o el
/// cronómetro local, según <see cref="TimerPreferences.Mode"/>. Los ajustes se leen en cada llamada,
/// así cualquier cambio se ve al momento sin volver a crear nada.
/// </summary>
public sealed class TimerSource
{
    private readonly UiPreferences _prefs;
    private readonly ZeldathonService _zeldathon;

    public TimerSource(UiPreferences prefs, ZeldathonService zeldathon)
    {
        _prefs = prefs;
        _zeldathon = zeldathon;
        Local = new LocalStopwatch(prefs.Timer.LocalElapsedMs);
        _zeldathon.Donations.Applied += Deltas.Add;
    }

    /// <summary>Cambios del reloj oficial por donaciones, para la animación del cronómetro.</summary>
    public TimeDeltaFeed Deltas { get; } = new();

    public LocalStopwatch Local { get; }

    /// <summary>Siempre el reloj oficial (lo que ve la página de Zeldatón), sin importar el modo elegido.</summary>
    public TimerDisplay CurrentOfficial() =>
        TimerDisplayBuilder.Official(_zeldathon.Clock, BuildOptions(), _zeldathon.State);

    public TimerDisplay Current()
    {
        var options = BuildOptions();
        return _prefs.Timer.IsLocal
            ? TimerDisplayBuilder.Local(Local, options)
            : TimerDisplayBuilder.Official(_zeldathon.Clock, options, _zeldathon.State);
    }

    private TimerDisplayOptions BuildOptions()
    {
        var t = _prefs.Timer;
        return new TimerDisplayOptions
        {
            Label = t.Label,
            Format = t.Format,
            WarnMinutes = t.WarnMinutes,
            CriticalMinutes = t.CriticalMinutes,
            BudgetSeconds = _zeldathon.BudgetSeconds,
            LocalCountdown = t.LocalCountdown,
            LocalStartMinutes = t.LocalStartMinutes,
        };
    }

    /// <summary>Guarda lo acumulado del cronómetro local (se restaura en pausa al volver a abrir).</summary>
    public void PersistLocal()
    {
        _prefs.Timer.LocalElapsedMs = Local.ElapsedMs;
        _prefs.Save();
    }

    /// <summary>Verde si suma tiempo, rojo si resta.</summary>
    public Color DeltaColor(long seconds)
    {
        var t = _prefs.Timer;
        return seconds >= 0
            ? OverlayLook.Parse(t.DeltaAddColor, Color.Parse("#5BD68A"))
            : OverlayLook.Parse(t.DeltaRemoveColor, Colors.IndianRed);
    }

    public Color ToneColor(TimerTone tone)
    {
        var t = _prefs.Timer;
        var theme = ThemeManager.Current;
        return tone switch
        {
            TimerTone.Warn => OverlayLook.Parse(t.WarnColor, OverlayLook.Parse(OverlayLook.Gold, Colors.Gold)),
            TimerTone.Critical => OverlayLook.Parse(t.CriticalColor, Colors.IndianRed),
            TimerTone.Exhausted => OverlayLook.Parse(t.ExhaustedColor, Colors.IndianRed),
            TimerTone.Paused => OverlayLook.Parse(t.PausedColor, OverlayLook.Title(_prefs)),
            TimerTone.Offline => OverlayLook.Parse(t.OfflineColor, OverlayLook.Title(_prefs)),
            _ => OverlayLook.Parse(t.NormalColor, OverlayLook.Number(_prefs)),
        };
    }
}
