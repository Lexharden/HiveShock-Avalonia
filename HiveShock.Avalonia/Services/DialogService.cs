using Avalonia.Controls;
using HiveShock.Avalonia.Views.Dialogs;
using HiveShock.Configuration;

namespace HiveShock.Avalonia.Services;

public sealed class DialogService
{
    public Window? Owner { get; set; }

    public void Info(string title, string message) =>
        _ = ShowMessageAsync(title, message, confirm: false, kind: "info");

    public void Warn(string title, string message) =>
        _ = ShowMessageAsync(title, message, confirm: false, kind: "warn");

    public void Error(string title, string message) =>
        _ = ShowMessageAsync(title, message, confirm: false, kind: "danger");

    public Task<bool> ConfirmAsync(string title, string message) =>
        ShowMessageAsync(title, message, confirm: true, kind: "warn");

    public async Task<bool> ConfirmTypedAsync(string title, string prompt, string expected)
    {
        var dlg = new ConfirmTextDialog(title, prompt, expected);
        return await ShowAsync(dlg);
    }

    public async Task<CatalogGift?> PickCatalogAsync(IReadOnlyList<CatalogGift> items)
    {
        var dlg = new CatalogPickDialog(items);
        return await ShowAsync(dlg) ? dlg.Selected : null;
    }

    public async Task<CatalogGift?> EditCatalogAsync(CatalogGift? existing)
    {
        var dlg = new CatalogEditDialog(existing);
        return await ShowAsync(dlg) ? dlg.Result : null;
    }

    private async Task<bool> ShowMessageAsync(string title, string message, bool confirm, string kind)
    {
        var dlg = new MessageDialog(title, message, confirm, kind);
        return await ShowAsync(dlg);
    }

    private async Task<bool> ShowAsync(Window dlg)
    {
        var owner = WindowOwnership.ResolveOwner(Owner);
        if (owner != null)
        {
            var result = await dlg.ShowDialog<bool?>(owner);
            return result == true;
        }

        dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var tcs = new TaskCompletionSource<bool>();
        dlg.Closed += (_, _) => tcs.TrySetResult(false);
        dlg.Show();
        return await tcs.Task;
    }
}
