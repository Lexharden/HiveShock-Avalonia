using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using HiveShock.Avalonia.Views.Overlays;
using HiveShock.Configuration;
using HiveShock.Hosting;
using HiveShock.Live;
using HiveShock.Overlays;

namespace HiveShock.Avalonia.Services;

/// <summary>Regalo de una meta tal como se dibuja en el overlay: su nombre y, si se tiene, su imagen del catálogo.</summary>
public sealed record GoalGiftInfo(string Name, string? ImagePath);

public sealed class OverlayService
{
    private CounterOverlayWindow? _counter;
    private GiftAlertOverlayWindow? _gifts;
    private GoalOverlayWindow? _goals;
    private bool _suppressClose;

    private OverlayFeed? _feed;

    public Window? Owner { get; set; }

    /// <summary>Servidor local que sirve los overlays a la «Fuente de navegador» de OBS.</summary>
    public WebOverlayServer Web { get; } = new();

    public OverlayFeed? Feed => _feed;

    /// <summary>Prepara los mensajes web. <paramref name="lookup"/> resuelve el regalo de una meta contra el catálogo.</summary>
    public void AttachFeed(Func<string, OverlayGiftRef?> lookup) => _feed = new OverlayFeed(Web, lookup);

    /// <summary>(Re)arranca el servidor según las preferencias. False si el puerto está ocupado.</summary>
    public bool StartWeb(UiPreferences prefs)
    {
        Web.Stop();
        if (!prefs.WebOverlayEnabled)
        {
            return true;
        }

        return Web.Start(prefs.ResolveWebOverlayPort(), prefs.WebOverlayLan, prefs.WebOverlayLan ? prefs.EnsureWebOverlayToken() : null);
    }

    /// <summary>Publica todo el estado a las páginas abiertas (al arrancar y al cambiar de perfil).</summary>
    public void PublishWeb(UiPreferences prefs, DeathCounter counter, IEnumerable<EditableGift> gifts, IEnumerable<GoalSnapshot> goals)
    {
        if (_feed == null)
        {
            return;
        }

        _feed.SetLook(OverlayLook.ToData(prefs));
        _feed.SetCounter(prefs.ResolveOverlayTitle(), counter.Value);
        _feed.SetGifts(gifts);
        _feed.SetGoals(goals);
    }

    public async Task CopyAsync(string text)
    {
        if (Owner is { } window && TopLevel.GetTopLevel(window)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    /// <summary>Resuelve la clave de un regalo de meta (id o nombre) contra el catálogo.</summary>
    public Func<string, GoalGiftInfo?>? GiftLookup { get; set; }

    public event Action? CounterClosedByUser;
    public event Action? GiftsClosedByUser;
    public event Action? GoalsClosedByUser;

    public void ShowCounter(DeathCounter counter, string title, UiPreferences prefs)
    {
        if (_counter == null)
        {
            _counter = new CounterOverlayWindow();
            _counter.Closed += (_, _) =>
            {
                if (_counter != null)
                {
                    prefs.OverlayLeft = _counter.Position.X;
                    prefs.OverlayTop = _counter.Position.Y;
                    prefs.Save();
                }

                _counter = null;
                if (!_suppressClose)
                {
                    CounterClosedByUser?.Invoke();
                }
            };
        }

        Place(_counter, prefs.OverlayLeft, prefs.OverlayTop);
        _counter.ApplyLook(prefs, prefs.ResolveOverlayScale());
        _counter.Apply(counter, title);
        _counter.Show();
    }

    public void HideCounter(UiPreferences prefs)
    {
        if (_counter == null)
        {
            return;
        }

        prefs.OverlayLeft = _counter.Position.X;
        prefs.OverlayTop = _counter.Position.Y;
        prefs.Save();
        _suppressClose = true;
        _counter.Close();
        _suppressClose = false;
        _counter = null;
    }

    public void RefreshCounter(DeathCounter counter, string title, UiPreferences? prefs = null)
    {
        _feed?.SetCounter(title, counter.Value);
        if (_counter == null)
        {
            return;
        }

        if (prefs != null)
        {
            _counter.ApplyLook(prefs, prefs.ResolveOverlayScale());
        }

        _counter.Apply(counter, title);
    }

    public void RefreshCounterScale(UiPreferences prefs) =>
        _counter?.ApplyLook(prefs, prefs.ResolveOverlayScale());

    public void RefreshGifts(IEnumerable<EditableGift> gifts, UiPreferences? prefs = null)
    {
        _feed?.SetGifts(gifts);
        if (_gifts == null)
        {
            return;
        }

        if (prefs != null)
        {
            _gifts.ApplyLook(prefs, prefs.ResolveOverlayScale(), gifts);
        }
        else
        {
            _gifts.Apply(gifts);
        }
    }

    public void ShowGifts(IEnumerable<EditableGift> gifts, UiPreferences prefs)
    {
        if (_gifts == null)
        {
            _gifts = new GiftAlertOverlayWindow();
            _gifts.Closed += (_, _) =>
            {
                if (_gifts != null)
                {
                    prefs.GiftOverlayLeft = _gifts.Position.X;
                    prefs.GiftOverlayTop = _gifts.Position.Y;
                    prefs.Save();
                }

                _gifts = null;
                if (!_suppressClose)
                {
                    GiftsClosedByUser?.Invoke();
                }
            };
        }

        Place(_gifts, prefs.GiftOverlayLeft, prefs.GiftOverlayTop);
        _gifts.ApplyLook(prefs, prefs.ResolveOverlayScale(), gifts);
        _gifts.Show();
    }

    public void HideGifts(UiPreferences prefs)
    {
        if (_gifts == null)
        {
            return;
        }

        prefs.GiftOverlayLeft = _gifts.Position.X;
        prefs.GiftOverlayTop = _gifts.Position.Y;
        prefs.Save();
        _suppressClose = true;
        _gifts.Close();
        _suppressClose = false;
        _gifts = null;
    }

    public void RefreshGiftsScale(UiPreferences prefs) =>
        _gifts?.ApplyLook(prefs, prefs.ResolveOverlayScale());

    public void RefreshOverlayScale(UiPreferences prefs)
    {
        _feed?.SetLook(OverlayLook.ToData(prefs));
        RefreshCounterScale(prefs);
        RefreshGiftsScale(prefs);
        RefreshGoalsScale(prefs);
    }

    public void RefreshLook(UiPreferences prefs, DeathCounter counter, IEnumerable<EditableGift> gifts)
    {
        _feed?.SetLook(OverlayLook.ToData(prefs));
        _feed?.SetCounter(prefs.ResolveOverlayTitle(), counter.Value);
        _feed?.SetGifts(gifts);
        _counter?.ApplyLook(prefs, prefs.ResolveOverlayScale());
        _counter?.Apply(counter, prefs.ResolveOverlayTitle());
        _gifts?.ApplyLook(prefs, prefs.ResolveOverlayScale(), gifts);
        _goals?.ApplyLook(prefs, prefs.ResolveOverlayScale());
    }

    public void ShowGoals(IEnumerable<GoalSnapshot> goals, UiPreferences prefs)
    {
        if (_goals == null)
        {
            _goals = new GoalOverlayWindow { GiftLookup = key => GiftLookup?.Invoke(key) };
            _goals.Closed += (_, _) =>
            {
                if (_goals != null)
                {
                    prefs.GoalOverlayLeft = _goals.Position.X;
                    prefs.GoalOverlayTop = _goals.Position.Y;
                    prefs.Save();
                }

                _goals = null;
                if (!_suppressClose)
                {
                    GoalsClosedByUser?.Invoke();
                }
            };
        }

        Place(_goals, prefs.GoalOverlayLeft, prefs.GoalOverlayTop);
        _goals.ApplyLook(prefs, prefs.ResolveOverlayScale());
        _goals.Apply(goals);
        _goals.Show();
    }

    public void HideGoals(UiPreferences prefs)
    {
        if (_goals == null)
        {
            return;
        }

        prefs.GoalOverlayLeft = _goals.Position.X;
        prefs.GoalOverlayTop = _goals.Position.Y;
        prefs.Save();
        _suppressClose = true;
        _goals.Close();
        _suppressClose = false;
        _goals = null;
    }

    public void RefreshGoals(IEnumerable<GoalSnapshot> goals, UiPreferences? prefs = null)
    {
        _feed?.SetGoals(goals);
        if (_goals == null)
        {
            return;
        }

        if (prefs != null)
        {
            _goals.ApplyLook(prefs, prefs.ResolveOverlayScale());
        }

        _goals.Apply(goals);
    }

    public void RefreshGoalsScale(UiPreferences prefs) =>
        _goals?.ApplyLook(prefs, prefs.ResolveOverlayScale());

    public void HighlightGift(string name, string? id)
    {
        _feed?.Highlight(name, id);
        _gifts?.Highlight(name, id);
    }

    public void CloseAll(UiPreferences prefs)
    {
        Web.Stop();
        _suppressClose = true;
        HideCounter(prefs);
        HideGifts(prefs);
        HideGoals(prefs);
        _suppressClose = false;
    }

    private static void Place(Window window, double left, double top)
    {
        if (double.IsNaN(left) || double.IsNaN(top))
        {
            return;
        }

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Position = new PixelPoint((int)left, (int)top);
    }
}
