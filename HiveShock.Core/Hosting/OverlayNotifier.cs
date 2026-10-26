namespace HiveShock.Hosting;

public sealed class OverlayGiftHitEventArgs : EventArgs
{
    public string GiftName { get; }
    public string GiftId { get; }

    public OverlayGiftHitEventArgs(string giftName, string giftId)
    {
        GiftName = giftName ?? "";
        GiftId = giftId ?? "";
    }
}

/// <summary>Algo que merece una alerta en pantalla (regalo, seguidor…): quién, qué y cuántos.</summary>
public sealed class OverlayAlertEventArgs : EventArgs
{
    public string Kind { get; }
    public string User { get; }
    public string Title { get; }
    public int Count { get; }
    public string GiftId { get; }

    public OverlayAlertEventArgs(string kind, string user, string title, int count = 1, string? giftId = null)
    {
        Kind = kind ?? "";
        User = user ?? "";
        Title = title ?? "";
        Count = Math.Max(1, count);
        GiftId = giftId ?? "";
    }
}

/// <summary>Aviso al overlay cuando un gift mapeado se dispara (live o Probar).</summary>
public sealed class OverlayNotifier
{
    public event EventHandler<OverlayGiftHitEventArgs>? GiftReceived;

    public event EventHandler<OverlayAlertEventArgs>? AlertRaised;

    public void Notify(string giftName, string? giftId) =>
        GiftReceived?.Invoke(this, new OverlayGiftHitEventArgs(giftName, giftId ?? ""));

    /// <summary>Alerta en pantalla: un regalo («gift»), un seguidor nuevo («follow»), etc.</summary>
    public void Alert(string kind, string user, string title = "", int count = 1, string? giftId = null) =>
        AlertRaised?.Invoke(this, new OverlayAlertEventArgs(kind, user, title, count, giftId));
}
