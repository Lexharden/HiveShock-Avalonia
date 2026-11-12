using Avalonia.Controls;
using Avalonia.Platform.Storage;
using HiveShock.Avalonia.Views.Dialogs;
using HiveShock.Configuration;

namespace HiveShock.Avalonia.Services;

public sealed class DialogService
{
    private static readonly FilePickerFileType JsonFileType = new("JSON") { Patterns = ["*.json"] };

    public Window? Owner { get; set; }

    /// <summary>Diálogo nativo para elegir un .json a importar. Null si el usuario canceló.</summary>
    public async Task<string?> PickOpenJsonAsync(string title)
    {
        var storage = ResolveStorageProvider();
        if (storage == null)
        {
            return null;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [JsonFileType],
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    /// <summary>Diálogo nativo para elegir dónde exportar un .json. Null si el usuario canceló.</summary>
    public async Task<string?> PickSaveJsonAsync(string title, string suggestedName)
    {
        var storage = ResolveStorageProvider();
        if (storage == null)
        {
            return null;
        }

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = "json",
            FileTypeChoices = [JsonFileType],
        });

        return file?.TryGetLocalPath();
    }

    private IStorageProvider? ResolveStorageProvider()
    {
        var owner = WindowOwnership.ResolveOwner(Owner);
        return owner != null ? TopLevel.GetTopLevel(owner)?.StorageProvider : null;
    }

    public void Info(string title, string message) =>
        _ = ShowMessageAsync(title, message, confirm: false, kind: "info");

    public void Warn(string title, string message) =>
        _ = ShowMessageAsync(title, message, confirm: false, kind: "warn");

    public void Error(string title, string message) =>
        _ = ShowMessageAsync(title, message, confirm: false, kind: "danger");

    public Task<bool> ConfirmAsync(string title, string message) =>
        ShowMessageAsync(title, message, confirm: true, kind: "warn");

    /// <summary>
    /// «¿Eliminar el regalo «Rose»?» con botones Cancelar / Eliminar. La usan Supr y los botones de papelera,
    /// así borrar pregunta igual se haga como se haga.
    /// </summary>
    /// <param name="what">Qué es, con artículo: «el regalo», «la meta», «el comando».</param>
    /// <param name="name">Su nombre, tal como se ve en la lista.</param>
    /// <param name="consequence">Una frase opcional con lo que deja de pasar.</param>
    public async Task<bool> ConfirmDeleteAsync(string what, string name, string? consequence = null)
    {
        var label = string.IsNullOrWhiteSpace(name) ? what : $"{what} «{name.Trim()}»";
        var message = $"¿Eliminar {label}?" +
                      (string.IsNullOrWhiteSpace(consequence) ? "" : Environment.NewLine + Environment.NewLine + consequence);
        var dlg = new MessageDialog("Eliminar", message, confirm: true, kind: "danger", okText: "Eliminar", cancelText: "Cancelar");
        return await ShowAsync(dlg);
    }

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

    public async Task<(string Name, ProfileTemplate Template)?> NewProfileAsync(string currentProfileName)
    {
        var dlg = new NewProfileDialog(currentProfileName);
        return await ShowAsync(dlg) ? (dlg.ProfileName, dlg.StartingPoint) : null;
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
