using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HiveShock.Configuration;

namespace HiveShock.Android.ViewModels;

public sealed partial class GiftsMapViewModel : ObservableObject
{
    private readonly MainViewModel _shell;

    public GiftsMapViewModel(MainViewModel shell) => _shell = shell;

    public ObservableCollection<GiftMapRow> Rows { get; } = [];
    public ObservableCollection<CatalogGift> CatalogPicks { get; } = [];
    public ObservableCollection<EffectItem> EffectChoices => _shell.Effects;

    [ObservableProperty] private GiftMapRow? _selected;
    [ObservableProperty] private CatalogGift? _catalogSelected;
    [ObservableProperty] private bool _catalogOpen;
    [ObservableProperty] private string _testComboText = "1";

    public bool HasSelection => Selected != null;

    partial void OnSelectedChanged(GiftMapRow? value) => OnPropertyChanged(nameof(HasSelection));

    public void LoadFrom(GiftFileEditor editor)
    {
        Rows.Clear();
        foreach (var gift in editor.Gifts)
        {
            Rows.Add(new GiftMapRow(gift));
        }

        Selected = Rows.FirstOrDefault();
    }

    public void ApplyTo(GiftFileEditor editor)
    {
        while (editor.Gifts.Count > 0)
        {
            editor.RemoveAt(0);
        }

        foreach (var row in Rows)
        {
            if (string.IsNullOrWhiteSpace(row.Effect) || !_shell.EffectExists(row.Effect))
            {
                row.Effect = _shell.DefaultEffectId();
            }

            if (row.MinCount < 1)
            {
                row.MinCount = 1;
            }

            editor.Add(row.Model);
        }
    }

    [RelayCommand]
    private void NewGift()
    {
        var gift = new EditableGift { Gift = "Nuevo", Effect = _shell.DefaultEffectId() };
        var row = new GiftMapRow(gift);
        Rows.Add(row);
        Selected = row;
    }

    [RelayCommand]
    private void DeleteGift()
    {
        if (Selected is null)
        {
            return;
        }

        Rows.Remove(Selected);
        Selected = Rows.FirstOrDefault();
    }

    [RelayCommand]
    private void OpenCatalog()
    {
        _shell.Runtime.ReloadCatalog();
        CatalogPicks.Clear();
        foreach (var gift in _shell.Runtime.Catalog.ListSorted())
        {
            CatalogPicks.Add(gift);
        }

        if (CatalogPicks.Count == 0)
        {
            _shell.Log("Todavía no hay regalos vistos. Conecta un live de TikTok.");
            CatalogOpen = false;
            return;
        }

        CatalogSelected = CatalogPicks.FirstOrDefault();
        CatalogOpen = true;
    }

    [RelayCommand]
    private void CancelCatalog() => CatalogOpen = false;

    [RelayCommand]
    private void PickCatalog()
    {
        if (CatalogSelected is null)
        {
            return;
        }

        AddFromCatalog(CatalogSelected);
        CatalogOpen = false;
    }

    [RelayCommand]
    private void Save() => _shell.SaveMappings();

    [RelayCommand]
    private async Task TestAsync()
    {
        if (Selected is null)
        {
            _shell.Log("Elige un regalo.");
            return;
        }

        if (string.IsNullOrWhiteSpace(Selected.Effect))
        {
            _shell.Log("Ese regalo no tiene efecto.");
            return;
        }

        if (!int.TryParse(TestComboText.Trim(), out var combo) || combo < 1)
        {
            combo = 1;
        }

        try
        {
            _shell.Runtime.SaveGameHost(_shell.GameHost);
            await _shell.Runtime.TestGiftAsync(Selected.Model, combo).ConfigureAwait(true);
            _shell.Log($"Prueba {Selected.Name} ×{combo}");
        }
        catch (Exception ex)
        {
            _shell.Log(ex.Message);
        }
    }

    private void AddFromCatalog(CatalogGift picked)
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
            _shell.Log($"Ya estaba: {existing.Name}");
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
            Effect = _shell.DefaultEffectId(),
        };
        var row = new GiftMapRow(gift);
        Rows.Add(row);
        Selected = row;
        _shell.Log($"Añadido {gift.Gift}. Pulsa Guardar.");
    }
}
