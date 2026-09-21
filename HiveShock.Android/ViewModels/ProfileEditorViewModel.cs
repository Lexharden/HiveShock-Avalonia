using System.Collections.ObjectModel;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HiveShock.Configuration;
using HiveShock.Logging;

namespace HiveShock.Android.ViewModels;

/// <summary>
/// Edita profile.json y effects.json del perfil activo. Import/export son independientes
/// por archivo, cada uno con su propio selector (SAF) y su propio Guardar.
/// </summary>
public sealed partial class ProfileEditorViewModel : ObservableObject
{
    private static readonly FilePickerFileType JsonFileType = new("JSON") { Patterns = ["*.json"] };
    private static readonly ObservableCollection<ProfileEffectPropertyRow> EmptyProperties = [];

    private readonly MainViewModel _shell;
    private ProfileInfoEditor? _profileEditor;
    private EffectCatalogEditor? _effectsEditor;

    public ProfileEditorViewModel(MainViewModel shell) => _shell = shell;

    [ObservableProperty] private string _displayName = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _helpNotes = "";
    [ObservableProperty] private string _gamePortText = "43000";
    [ObservableProperty] private string _ccPortText = "43001";
    [ObservableProperty] private string _eventPortText = "43002";
    [ObservableProperty] private bool _supportsDeathEvents = true;
    [ObservableProperty] private bool _supportsDeleteSave;
    [ObservableProperty] private bool _supportsRescue;
    [ObservableProperty] private ProfileLabelRow? _selectedLabel;
    [ObservableProperty] private ProfileEffectRow? _selectedEffect;
    [ObservableProperty] private ProfileEffectPropertyRow? _selectedProperty;

    public ObservableCollection<ProfileLabelRow> Labels { get; } = [];
    public ObservableCollection<ProfileEffectRow> Effects { get; } = [];

    public ObservableCollection<ProfileEffectPropertyRow> SelectedProperties =>
        SelectedEffect?.Properties ?? EmptyProperties;

    public bool HasLabelSelection => SelectedLabel != null;
    public bool HasEffectSelection => SelectedEffect != null;
    public bool HasPropertySelection => SelectedProperty != null;
    public bool CanEdit => !_shell.Runtime.IsRunning;

    public void RefreshCanEdit() => OnPropertyChanged(nameof(CanEdit));

    partial void OnSelectedLabelChanged(ProfileLabelRow? value) => OnPropertyChanged(nameof(HasLabelSelection));

    partial void OnSelectedEffectChanged(ProfileEffectRow? value)
    {
        OnPropertyChanged(nameof(HasEffectSelection));
        OnPropertyChanged(nameof(SelectedProperties));
        SelectedProperty = value?.Properties.FirstOrDefault();
    }

    partial void OnSelectedPropertyChanged(ProfileEffectPropertyRow? value) =>
        OnPropertyChanged(nameof(HasPropertySelection));

    public void Load()
    {
        var profile = _shell.Runtime.Profile;
        _profileEditor = new ProfileInfoEditor(profile.ProfileJsonPath);
        _profileEditor.Load();
        _effectsEditor = new EffectCatalogEditor(_shell.Runtime.EffectsPath);
        _effectsEditor.Load();
        ApplyProfileToUi();
        ApplyEffectsToUi();
        OnPropertyChanged(nameof(CanEdit));
    }

    private void ApplyProfileToUi()
    {
        var info = _profileEditor!.Info;
        DisplayName = info.DisplayName;
        Description = info.Description;
        HelpNotes = info.HelpNotes;
        GamePortText = info.GamePort.ToString();
        CcPortText = info.CrowdControlPort.ToString();
        EventPortText = info.EventPort.ToString();
        SupportsDeathEvents = info.SupportsDeathEvents;
        SupportsDeleteSave = info.SupportsDeleteSave;
        SupportsRescue = info.SupportsRescue;

        Labels.Clear();
        foreach (var label in info.Labels)
        {
            Labels.Add(new ProfileLabelRow(label));
        }

        SelectedLabel = Labels.FirstOrDefault();
    }

    private void ApplyEffectsToUi()
    {
        var enemySet = new HashSet<string>(_profileEditor!.Info.EnemyEffects, StringComparer.OrdinalIgnoreCase);
        var singletonSet = new HashSet<string>(_profileEditor.Info.SingletonEffects, StringComparer.OrdinalIgnoreCase);

        Effects.Clear();
        foreach (var effect in _effectsEditor!.Effects)
        {
            var row = new ProfileEffectRow(effect)
            {
                IsEnemy = enemySet.Contains(effect.Id),
                IsSingleton = singletonSet.Contains(effect.Id),
            };
            foreach (var prop in effect.Properties)
            {
                row.Properties.Add(new ProfileEffectPropertyRow(prop));
            }

            Effects.Add(row);
        }

        SelectedEffect = Effects.FirstOrDefault();
    }

    private bool EnsureEditable()
    {
        if (CanEdit)
        {
            return true;
        }

        _shell.Log("Detén el bridge antes de editar el perfil.");
        return false;
    }

    [RelayCommand]
    private void AddLabel()
    {
        var model = new EditableLabel { Key = "efecto_id", Value = "Nombre para mostrar" };
        var row = new ProfileLabelRow(model);
        Labels.Add(row);
        SelectedLabel = row;
    }

    [RelayCommand]
    private void RemoveLabel()
    {
        if (SelectedLabel is null)
        {
            return;
        }

        Labels.Remove(SelectedLabel);
        SelectedLabel = Labels.FirstOrDefault();
    }

    [RelayCommand]
    private void AddEffect()
    {
        var model = new EditableEffect { Id = "nuevo_efecto" };
        model.Properties.Add(new EditableEffectProperty { Key = "action", Value = "nuevo_efecto" });
        var row = new ProfileEffectRow(model);
        row.Properties.Add(new ProfileEffectPropertyRow(model.Properties[0]));
        Effects.Add(row);
        SelectedEffect = row;
    }

    [RelayCommand]
    private void RemoveEffect()
    {
        if (SelectedEffect is null)
        {
            return;
        }

        Effects.Remove(SelectedEffect);
        SelectedEffect = Effects.FirstOrDefault();
    }

    [RelayCommand]
    private void AddProperty()
    {
        if (SelectedEffect is null)
        {
            return;
        }

        var prop = new EditableEffectProperty { Key = "clave", Value = "" };
        SelectedEffect.Model.Properties.Add(prop);
        var row = new ProfileEffectPropertyRow(prop);
        SelectedEffect.Properties.Add(row);
        SelectedProperty = row;
    }

    [RelayCommand]
    private void RemoveProperty()
    {
        if (SelectedEffect is null || SelectedProperty is null)
        {
            return;
        }

        SelectedEffect.Model.Properties.Remove(SelectedProperty.Model);
        SelectedEffect.Properties.Remove(SelectedProperty);
        SelectedProperty = SelectedEffect.Properties.FirstOrDefault();
    }

    [RelayCommand]
    private void SaveProfile()
    {
        if (!EnsureEditable() || _profileEditor is null)
        {
            return;
        }

        try
        {
            PullProfileFromUi();
            _profileEditor.Save();
            _shell.Runtime.ReloadProfileMeta();
            Load();
            _shell.Log("profile.json guardado.");
        }
        catch (Exception ex)
        {
            _shell.Log(ex.Message);
        }
    }

    [RelayCommand]
    private void SaveEffects()
    {
        if (!EnsureEditable() || _effectsEditor is null)
        {
            return;
        }

        var ids = Effects.Select(e => e.Id.Trim()).Where(id => id.Length > 0).ToList();
        var dupes = ids.GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dupes.Count > 0)
        {
            _shell.Log($"Ids repetidos: {string.Join(", ", dupes)}. Corrígelos antes de guardar.");
            return;
        }

        try
        {
            PullEffectsFromUi();
            _effectsEditor.Save();
            _shell.Runtime.ReloadProfileMeta();
            Load();
            _shell.Log("effects.json guardado.");
        }
        catch (Exception ex)
        {
            _shell.Log(ex.Message);
        }
    }

    private void PullProfileFromUi()
    {
        var info = _profileEditor!.Info;
        info.DisplayName = DisplayName;
        info.Description = Description;
        info.HelpNotes = HelpNotes;
        info.GamePort = ParseInt(GamePortText, info.GamePort);
        info.CrowdControlPort = ParseInt(CcPortText, info.CrowdControlPort);
        info.EventPort = ParseInt(EventPortText, info.EventPort);
        info.SupportsDeathEvents = SupportsDeathEvents;
        info.SupportsDeleteSave = SupportsDeleteSave;
        info.SupportsRescue = SupportsRescue;

        info.Labels.Clear();
        foreach (var row in Labels)
        {
            if (string.IsNullOrWhiteSpace(row.Key))
            {
                continue;
            }

            info.Labels.Add(new EditableLabel { Key = row.Key.Trim(), Value = row.Value });
        }

        info.EnemyEffects.Clear();
        info.EnemyEffects.AddRange(Effects.Where(e => e.IsEnemy).Select(e => e.Id.Trim()));
        info.SingletonEffects.Clear();
        info.SingletonEffects.AddRange(Effects.Where(e => e.IsSingleton).Select(e => e.Id.Trim()));
    }

    private void PullEffectsFromUi()
    {
        var list = new List<EditableEffect>();
        foreach (var row in Effects)
        {
            if (string.IsNullOrWhiteSpace(row.Id))
            {
                continue;
            }

            row.Model.Id = row.Id.Trim();
            list.Add(row.Model);
        }

        _effectsEditor!.ReplaceInMemory(list);
    }

    private static int ParseInt(string text, int fallback) =>
        int.TryParse(text.Trim(), out var n) && n > 0 ? n : fallback;

    [RelayCommand]
    private async Task ImportProfileAsync()
    {
        if (!EnsureEditable() || _profileEditor is null)
        {
            return;
        }

        var provider = AndroidStorage.Provider;
        if (provider is null)
        {
            return;
        }

        var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Importar profile.json",
            AllowMultiple = false,
            FileTypeFilter = [JsonFileType],
        });
        if (files.Count == 0)
        {
            return;
        }

        try
        {
            await using var stream = await files[0].OpenReadAsync();
            var imported = await ProfileInfoEditor.ImportFromAsync(stream);
            _profileEditor.ReplaceInMemory(imported);
            ApplyProfileToUi();
            _shell.Log("profile.json importado. Revisa y pulsa Guardar para aplicarlo.");
        }
        catch (Exception ex)
        {
            _shell.Log(ex.Message);
        }
    }

    [RelayCommand]
    private async Task ExportProfileAsync()
    {
        if (_profileEditor is null)
        {
            return;
        }

        var provider = AndroidStorage.Provider;
        if (provider is null)
        {
            return;
        }

        var suggested = string.IsNullOrWhiteSpace(_profileEditor.Info.Id) ? "profile" : $"{_profileEditor.Info.Id}-profile";
        var file = await provider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Exportar profile.json",
            SuggestedFileName = $"{suggested}.json",
            DefaultExtension = "json",
            FileTypeChoices = [JsonFileType],
        });
        if (file is null)
        {
            return;
        }

        try
        {
            PullProfileFromUi();
            await using var stream = await file.OpenWriteAsync();
            await _profileEditor.ExportToAsync(stream);
            _shell.Log("profile.json exportado.");
        }
        catch (Exception ex)
        {
            _shell.Log(ex.Message);
        }
    }

    [RelayCommand]
    private async Task ImportEffectsAsync()
    {
        if (!EnsureEditable() || _effectsEditor is null)
        {
            return;
        }

        var provider = AndroidStorage.Provider;
        if (provider is null)
        {
            return;
        }

        var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Importar effects.json",
            AllowMultiple = false,
            FileTypeFilter = [JsonFileType],
        });
        if (files.Count == 0)
        {
            return;
        }

        try
        {
            await using var stream = await files[0].OpenReadAsync();
            var imported = await EffectCatalogEditor.ImportFromAsync(stream);
            _effectsEditor.ReplaceInMemory(imported);
            ApplyEffectsToUi();
            _shell.Log("effects.json importado. Revisa y pulsa Guardar para aplicarlo.");
        }
        catch (Exception ex)
        {
            _shell.Log(ex.Message);
        }
    }

    [RelayCommand]
    private async Task ExportEffectsAsync()
    {
        if (_effectsEditor is null)
        {
            return;
        }

        var provider = AndroidStorage.Provider;
        if (provider is null)
        {
            return;
        }

        var suggested = string.IsNullOrWhiteSpace(_profileEditor?.Info.Id) ? "effects" : $"{_profileEditor!.Info.Id}-effects";
        var file = await provider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Exportar effects.json",
            SuggestedFileName = $"{suggested}.json",
            DefaultExtension = "json",
            FileTypeChoices = [JsonFileType],
        });
        if (file is null)
        {
            return;
        }

        try
        {
            PullEffectsFromUi();
            await using var stream = await file.OpenWriteAsync();
            await _effectsEditor.ExportToAsync(stream);
            _shell.Log("effects.json exportado.");
        }
        catch (Exception ex)
        {
            _shell.Log(ex.Message);
        }
    }
}
