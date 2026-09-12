using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HiveShock.Avalonia.Services;
using HiveShock.Configuration;
using HiveShock;
using HiveShock.Logging;

namespace HiveShock.Avalonia.ViewModels;

public sealed partial class CatalogViewModel : ViewModelBase
{
    private readonly MainViewModel _shell;

    public CatalogViewModel(MainViewModel shell)
    {
        _shell = shell;
        Load();
    }

    public ObservableCollection<CatalogGift> Rows { get; } = [];

    [ObservableProperty] private CatalogGift? _selected;

    public void Load()
    {
        _shell.Runtime.ReloadCatalog();
        GiftImageLoader.ClearCache();
        Rows.Clear();
        foreach (var g in _shell.Runtime.Catalog.ListSorted())
        {
            Rows.Add(g);
        }

        _shell.Studio.RefreshStats();
    }

    [RelayCommand]
    private void Reload() => Load();

    [RelayCommand]
    private Task NewItemAsync() => SaveManualAsync(null);

    [RelayCommand]
    private async Task EditAsync()
    {
        if (Selected == null)
        {
            _shell.Dialogs.Info("Vistos", "Elige un regalo para editarlo.");
            return;
        }

        await SaveManualAsync(Selected);
    }

    private async Task SaveManualAsync(CatalogGift? existing)
    {
        var result = await _shell.Dialogs.EditCatalogAsync(existing);
        if (result == null)
        {
            return;
        }

        try
        {
            _shell.Runtime.Catalog.UpsertManual(result.Id, result.NameEn, result.NameEs, result.Diamonds, existing);
            Load();
            BridgeLog.Info($"Vistos {(existing == null ? "+" : "~")} {result.DisplayName}");
        }
        catch (Exception ex)
        {
            _shell.Dialogs.Warn("Vistos", ex.Message);
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        try
        {
            var dir = Path.GetDirectoryName(_shell.Runtime.Catalog.Path);
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            {
                dir = AppPaths.AppDirectory;
            }

            PlatformShell.OpenFolder(dir);
        }
        catch (Exception ex)
        {
            _shell.Dialogs.Warn("Carpeta", ex.Message);
        }
    }

    [RelayCommand]
    private void AddToGifts()
    {
        if (Selected == null)
        {
            _shell.Dialogs.Info("Vistos", "Elige un regalo para usarlo en este juego.");
            return;
        }

        _shell.Gifts.AddFromCatalog(Selected);
        _shell.GoGifts(reload: false);
    }
}
