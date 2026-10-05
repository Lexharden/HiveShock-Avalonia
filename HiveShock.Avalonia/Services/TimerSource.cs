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
        Local = new LocalStopwatch(prefs.Timer.LocalElapsedMs, offsetMs: prefs.Timer.LocalOffsetMs);
        _zeldathon.Donations.Applied += OnServerDonationApplied;
        _zeldathon.Donations.LocalTimer = new LocalTimerHook(() => _prefs.Timer.IsLocal, ApplyLocalDonation);
    }

    private readonly object _localGate = new();

    /// <summary>Cambios del reloj oficial por donaciones, para la animación del cronómetro.</summary>
    public TimeDeltaFeed Deltas { get; } = new();

    public LocalStopwatch Local { get; }

    /// <summary>Siempre el reloj oficial (lo que ve la página de Zeldatón), sin importar el modo elegido.</summary>
    public TimerDisplay CurrentOfficial() =>
        TimerDisplayBuilder.Official(_zeldathon.Clock, BuildOptions(), _zeldathon.State);

    /// <summary>Siempre el cronómetro manual, con lo que sumaron o restaron las donaciones.</summary>
    public TimerDisplay CurrentLocal() => TimerDisplayBuilder.Local(Local, BuildOptions());

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
        _prefs.Timer.LocalOffsetMs = Local.OffsetMs;
        _prefs.Save();
    }

    /// <summary>
    /// El servidor confirma cambios del reloj oficial. Con el cronómetro manual en pantalla no se anima
    /// (ese ya se anima al aplicarle la donación), así no salen dos avisos por la misma donación.
    /// </summary>
    private void OnServerDonationApplied(DonationTimeApplied applied)
    {
        if (!_prefs.Timer.IsLocal)
        {
            Deltas.Add(applied);
        }
    }

    /// <summary>
    /// Una donación suma o resta al cronómetro manual (esté corriendo o en pausa). El número mostrado nunca
    /// baja de cero: si la resta lo pasaría, solo se descuenta lo que queda. Devuelve los segundos que de
    /// verdad cambió y anima el aviso con ese valor.
    /// </summary>
    private long ApplyLocalDonation(long requestedSeconds)
    {
        long changeMs;
        var wantedMs = requestedSeconds * 1000;
        lock (_localGate)
        {
            var current = TimerDisplayBuilder.LocalValueMs(Local, BuildOptions());
            changeMs = Math.Max(0, current + wantedMs) - current;
            Local.Adjust(changeMs);
        }

        var applied = (long)Math.Round(changeMs / 1000.0, MidpointRounding.AwayFromZero);
        Deltas.Push(applied, limited: changeMs != wantedMs);
        return applied;
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
