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

/// <summary>Aviso al overlay cuando un gift mapeado se dispara (live o Probar).</summary>
public sealed class OverlayNotifier
{
    public event EventHandler<OverlayGiftHitEventArgs>? GiftReceived;

    public void Notify(string giftName, string? giftId) =>
        GiftReceived?.Invoke(this, new OverlayGiftHitEventArgs(giftName, giftId ?? ""));
}
