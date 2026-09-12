using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HiveShock.Avalonia.Services;
using HiveShock.Configuration;
using HiveShock;
using HiveShock.Logging;

namespace HiveShock.Avalonia.ViewModels;

public sealed partial class GiftsViewModel : ViewModelBase
{
    private readonly MainViewModel _shell;
    private GiftFileEditor _editor;

    public GiftsViewModel(MainViewModel shell)
    {
        _shell = shell;
        _editor = new GiftFileEditor(shell.Runtime.GiftsPath);
        Load();
    }

    public ObservableCollection<GiftRowViewModel> Rows { get; } = [];

    public IEnumerable<EditableGift> Models => Rows.Select(r => r.Model);

    public ObservableCollection<EffectChoice> EffectChoices => _shell.EffectChoices;

    [ObservableProperty] private GiftRowViewModel? _selected;
    [ObservableProperty] private string _testComboText = "1";

    public GiftFileEditor Editor => _editor;

    public bool HasSelection => Selected != null;

    partial void OnSelectedChanged(GiftRowViewModel? value) => OnPropertyChanged(nameof(HasSelection));

    public void Load()
    {
        try
        {
            _editor = new GiftFileEditor(_shell.Runtime.GiftsPath);
            _editor.Load();
            Rows.Clear();
            foreach (var g in _editor.Gifts)
            {
                Rows.Add(Wrap(g));
            }

            Selected = Rows.FirstOrDefault();
            _shell.Events?.LoadFromEditor(_editor);
            _shell.Overlays.RefreshGifts(Models);
        }
        catch (Exception ex)
        {
            BridgeLog.Warn($"gifts.json: {ex.Message}");
        }
    }

    public void SaveToDisk()
    {
        SyncEditorFromRows();
        _shell.Events?.ApplyToEditor(_editor);
        _editor.Save();
        _shell.Runtime.ReloadGifts();
        GiftImageLoader.ClearCache();
        foreach (var row in Rows)
        {
            row.RefreshThumb();
        }

        _shell.Studio.RefreshStats();
        _shell.Overlays.RefreshGifts(Models);
        BridgeLog.Info("Regalos guardados.");
    }

    private void SyncEditorFromRows()
    {
        while (_editor.Gifts.Count > 0)
        {
            _editor.RemoveAt(0);
        }

        foreach (var row in Rows)
        {
            if (string.IsNullOrWhiteSpace(row.Effect) || !EffectExists(row.Effect))
            {
                row.Effect = DefaultEffectId();
            }

            if (row.MinCount < 1)
            {
                row.MinCount = 1;
            }

            _editor.Add(row.Model);
        }
    }

    private GiftRowViewModel Wrap(EditableGift gift)
    {
        var row = new GiftRowViewModel(gift);
        row.EffectLabel = LabelFor(gift.Effect);
        return row;
    }

    private string LabelFor(string effect)
    {
        var hit = _shell.EffectChoices.FirstOrDefault(e =>
            string.Equals(e.Id, effect, StringComparison.OrdinalIgnoreCase));
        return hit?.Display ?? effect;
    }

    private bool EffectExists(string effectId) =>
        _shell.EffectChoices.Any(e =>
            string.Equals(e.Id, effectId, StringComparison.OrdinalIgnoreCase));

    private string DefaultEffectId()
    {
        var impulse = _shell.EffectChoices.FirstOrDefault(e =>
            string.Equals(e.Id, "impulse", StringComparison.OrdinalIgnoreCase));
        if (impulse != null)
        {
            return impulse.Id;
        }

        return _shell.EffectChoices.FirstOrDefault()?.Id ?? "";
    }

    public void AddFromCatalog(CatalogGift picked)
    {
        var primary = picked.PrimaryName;
        if (string.IsNullOrWhiteSpace(primary))
        {
            primary = picked.DisplayName;
        }

        var existing = Rows.FirstOrDefault(r =>
            (!string.IsNullOrWhiteSpace(picked.Id) &&
             string.Equals(r.Id, picked.Id, StringComparison.OrdinalIgnoreCase)) ||
            string.Equals(r.Name, primary, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            Selected = existing;
            BridgeLog.Info($"Ya estaba en regalos: {existing.Name}");
            return;
        }

        var also = new List<string>();
        void AddAlias(string? name)
        {
            if (string.IsNullOrWhiteSpace(name) ||
                string.Equals(name, primary, StringComparison.OrdinalIgnoreCase) ||
                also.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            also.Add(name);
        }

        AddAlias(picked.NameEn);
        AddAlias(picked.NameEs);
        foreach (var extra in picked.Also)
        {
            AddAlias(extra);
        }

        var gift = new EditableGift
        {
            Gift = primary,
            Id = picked.Id,
            Also = also,
            Diamonds = picked.Diamonds,
            Effect = DefaultEffectId(),
            What = "",
        };
        var row = Wrap(gift);
        Rows.Add(row);
        Selected = row;
        _shell.Overlays.RefreshGifts(Models);
        BridgeLog.Info($"Añadido a regalos: {gift.Gift} (pulsa Guardar)");
    }

    [RelayCommand]
    private void NewGift()
    {
        var gift = new EditableGift { Gift = "Nuevo", Effect = DefaultEffectId() };
        var row = Wrap(gift);
        Rows.Add(row);
        Selected = row;
    }

    [RelayCommand]
    private async Task FromCatalogAsync()
    {
        _shell.Runtime.ReloadCatalog();
        var list = _shell.Runtime.Catalog.ListSorted();
        if (list.Count == 0)
        {
            _shell.Dialogs.Info("Vistos", "Todavía no hay regalos. Conecta en “Anotar regalos” durante un live.");
            return;
        }

        var picked = await _shell.Dialogs.PickCatalogAsync(list);
        if (picked == null)
        {
            return;
        }

        AddFromCatalog(picked);
    }

    [RelayCommand]
    private void DeleteGift()
    {
        if (Selected == null)
        {
            return;
        }

        Rows.Remove(Selected);
        Selected = Rows.FirstOrDefault();
    }

    [RelayCommand]
    private void Reload()
    {
        GiftImageLoader.ClearCache();
        Load();
        _shell.Studio.RefreshStats();
    }

    [RelayCommand]
    private void Save()
    {
        try
        {
            SaveToDisk();
        }
        catch (Exception ex)
        {
            _shell.Dialogs.Error("Regalos", ex.Message);
        }
    }

    [RelayCommand]
    private async Task TestAsync()
    {
        if (Selected == null)
        {
            _shell.Dialogs.Info("Probar", "Elige un regalo de la lista.");
            return;
        }

        if (string.IsNullOrWhiteSpace(Selected.Effect))
        {
            _shell.Dialogs.Warn("Probar", "Ese regalo no tiene efecto. Elige qué hace y guarda.");
            return;
        }

        if (!int.TryParse(TestComboText.Trim(), out var combo) || combo < 1)
        {
            combo = 1;
        }

        try
        {
            await _shell.Runtime.TestGiftAsync(Selected.Model, combo).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _shell.Dialogs.Error("Probar", ex.Message);
        }
    }

    [RelayCommand]
    private void OpenImages()
    {
        try
        {
            GiftImages.EnsureDirectory();
            PlatformShell.OpenFolder(GiftImages.DirectoryPath);
        }
        catch (Exception ex)
        {
            _shell.Dialogs.Warn("Imágenes", ex.Message);
        }
    }
}
