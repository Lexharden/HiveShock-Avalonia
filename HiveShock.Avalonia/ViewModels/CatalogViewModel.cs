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

    /// <summary>Incluir los regalos de tiktok_gifts.json que aún no han salido en ningún live.</summary>
    [ObservableProperty] private bool _showAllTikTokGifts = true;

    [ObservableProperty] private string _searchText = "";

    [ObservableProperty] private string _summary = "";

    private IReadOnlyList<CatalogGift> _all = [];

    partial void OnShowAllTikTokGiftsChanged(bool value) => Load();

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    public void Load()
    {
        _shell.Runtime.ReloadCatalog();
        GiftImages.Invalidate();
        GiftImageLoader.ClearCache();
        _all = _shell.Runtime.Catalog.ListSorted(ShowAllTikTokGifts);
        ApplyFilter();
        _shell.Studio.RefreshStats();
    }

    /// <summary>Filtra por nombre (EN, ES o alias) o por id, sin recargar el catálogo.</summary>
    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        Rows.Clear();
        foreach (var g in _all)
        {
            if (g.Matches(query))
            {
                Rows.Add(g);
            }
        }

        var seen = _all.Count(g => !g.IsReferenceOnly);
        Summary = query.Length == 0
            ? $"{_all.Count} regalos · {seen} vistos en tus lives"
            : $"{Rows.Count} de {_all.Count} regalos";
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
            _shell.Dialogs.Info("Catálogo", "Elige un regalo para editarlo.");
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
            BridgeLog.Info($"Catálogo {(existing == null ? "+" : "~")} {result.DisplayName}");
        }
        catch (Exception ex)
        {
            _shell.Dialogs.Warn("Catálogo", ex.Message);
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
            _shell.Dialogs.Info("Catálogo", "Elige un regalo para usarlo en este juego.");
            return;
        }

        _shell.Gifts.AddFromCatalog(Selected);
        _shell.GoGifts(reload: false);
    }
}
