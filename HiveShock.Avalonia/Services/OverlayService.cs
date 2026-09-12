using Avalonia;
using Avalonia.Controls;
using HiveShock.Avalonia.Views.Overlays;
using HiveShock.Configuration;
using HiveShock.Hosting;

namespace HiveShock.Avalonia.Services;

public sealed class OverlayService
{
    private CounterOverlayWindow? _counter;
    private GiftAlertOverlayWindow? _gifts;
    private bool _suppressClose;

    public Window? Owner { get; set; }

    public event Action? CounterClosedByUser;
    public event Action? GiftsClosedByUser;

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
        RefreshCounterScale(prefs);
        RefreshGiftsScale(prefs);
    }

    public void RefreshLook(UiPreferences prefs, DeathCounter counter, IEnumerable<EditableGift> gifts)
    {
        _counter?.ApplyLook(prefs, prefs.ResolveOverlayScale());
        _counter?.Apply(counter, prefs.ResolveOverlayTitle());
        _gifts?.ApplyLook(prefs, prefs.ResolveOverlayScale(), gifts);
    }

    public void HighlightGift(string name, string? id) => _gifts?.Highlight(name, id);

    public void CloseAll(UiPreferences prefs)
    {
        _suppressClose = true;
        HideCounter(prefs);
        HideGifts(prefs);
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
