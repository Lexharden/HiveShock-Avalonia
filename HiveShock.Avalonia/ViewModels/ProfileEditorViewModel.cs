using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HiveShock.Configuration;
using HiveShock.Logging;

namespace HiveShock.Avalonia.ViewModels;

public sealed partial class LabelRow : ObservableObject
{
    public LabelRow(EditableLabel model)
    {
        Model = model;
        _key = model.Key;
        _value = model.Value;
    }

    public EditableLabel Model { get; }

    [ObservableProperty] private string _key = "";
    [ObservableProperty] private string _value = "";

    partial void OnKeyChanged(string value) => Model.Key = value;
    partial void OnValueChanged(string value) => Model.Value = value;
}

public sealed partial class EffectPropertyRow : ObservableObject
{
    public static IReadOnlyList<EditableValueKind> KindOptions { get; } = Enum.GetValues<EditableValueKind>();

    public EffectPropertyRow(EditableEffectProperty model)
    {
        Model = model;
        _key = model.Key;
        _value = model.Value;
        _kind = model.Kind;
    }

    public EditableEffectProperty Model { get; }

    [ObservableProperty] private string _key = "";
    [ObservableProperty] private string _value = "";
    [ObservableProperty] private EditableValueKind _kind;

    partial void OnKeyChanged(string value) => Model.Key = value;
    partial void OnValueChanged(string value) => Model.Value = value;
    partial void OnKindChanged(EditableValueKind value) => Model.Kind = value;
}

public sealed partial class EffectRow : ObservableObject
{
    public EffectRow(EditableEffect model)
    {
        Model = model;
        _id = model.Id;
    }

    public EditableEffect Model { get; }

    [ObservableProperty] private string _id = "";
    [ObservableProperty] private bool _isEnemy;
    [ObservableProperty] private bool _isSingleton;
    [ObservableProperty] private EffectPropertyRow? _selectedProperty;

    public ObservableCollection<EffectPropertyRow> Properties { get; } = [];

    partial void OnIdChanged(string value) => Model.Id = value;

    [RelayCommand]
    private void AddProperty()
    {
        var prop = new EditableEffectProperty { Key = "clave", Value = "" };
        Model.Properties.Add(prop);
        var row = new EffectPropertyRow(prop);
        Properties.Add(row);
        SelectedProperty = row;
    }

    [RelayCommand]
    private void RemoveProperty()
    {
        if (SelectedProperty is null)
        {
            return;
        }

        Model.Properties.Remove(SelectedProperty.Model);
        Properties.Remove(SelectedProperty);
        SelectedProperty = Properties.FirstOrDefault();
    }
}

/// <summary>
/// Edita profile.json y effects.json del perfil activo. Import/export son independientes
/// por archivo: cada uno se lee/escribe con su propio diálogo y su propio Guardar, para no
/// mezclar dos formatos distintos en una sola operación.
/// </summary>
public sealed partial class ProfileEditorViewModel : ViewModelBase
{
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
    [ObservableProperty] private LabelRow? _selectedLabel;
    [ObservableProperty] private EffectRow? _selectedEffect;

    public ObservableCollection<LabelRow> Labels { get; } = [];
    public ObservableCollection<EffectRow> Effects { get; } = [];

    public bool CanEdit => !_shell.IsRunning;

    public void Load()
    {
        var profile = _shell.Runtime.Profile;
        _profileEditor = new ProfileInfoEditor(profile.ProfileJsonPath);
        _profileEditor.Load();
        _effectsEditor = new EffectCatalogEditor(_shell.Runtime.EffectsPath);
        _effectsEditor.Load();
        ApplyProfileToUi();
        ApplyEffectsToUi();
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
            Labels.Add(new LabelRow(label));
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
            var row = new EffectRow(effect)
            {
                IsEnemy = enemySet.Contains(effect.Id),
                IsSingleton = singletonSet.Contains(effect.Id),
            };
            foreach (var prop in effect.Properties)
            {
                row.Properties.Add(new EffectPropertyRow(prop));
            }

            Effects.Add(row);
        }

        SelectedEffect = Effects.FirstOrDefault();
    }

    private bool EnsureEditable(string what)
    {
        if (CanEdit)
        {
            return true;
        }

        _shell.Dialogs.Warn(what, "Detén el bridge antes de editar el perfil.");
        return false;
    }

    [RelayCommand]
    private void AddLabel()
    {
        var model = new EditableLabel { Key = "efecto_id", Value = "Nombre para mostrar" };
        var row = new LabelRow(model);
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
        var row = new EffectRow(model);
        row.Properties.Add(new EffectPropertyRow(model.Properties[0]));
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
    private void SaveProfile()
    {
        if (!EnsureEditable("Perfil") || _profileEditor is null)
        {
            return;
        }

        try
        {
            PullProfileFromUi();
            _profileEditor.Save();
            _shell.Runtime.ReloadProfileMeta();
            _shell.OnProfileChanged();
            Load();
            BridgeLog.Info("profile.json guardado.");
        }
        catch (Exception ex)
        {
            _shell.Dialogs.Error("Perfil", ex.Message);
        }
    }

    [RelayCommand]
    private void SaveEffects()
    {
        if (!EnsureEditable("Efectos") || _effectsEditor is null)
        {
            return;
        }

        var ids = Effects.Select(e => e.Id.Trim()).Where(id => id.Length > 0).ToList();
        var dupes = ids.GroupBy(id => id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dupes.Count > 0)
        {
            _shell.Dialogs.Warn("Efectos", $"Hay ids repetidos: {string.Join(", ", dupes)}. Corrígelos antes de guardar.");
            return;
        }

        try
        {
            PullEffectsFromUi();
            _effectsEditor.Save();
            _shell.Runtime.ReloadProfileMeta();
            _shell.OnProfileChanged();
            Load();
            BridgeLog.Info("effects.json guardado.");
        }
        catch (Exception ex)
        {
            _shell.Dialogs.Error("Efectos", ex.Message);
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
        if (!EnsureEditable("Perfil") || _profileEditor is null)
        {
            return;
        }

        var path = await _shell.Dialogs.PickOpenJsonAsync("Importar profile.json");
        if (path is null)
        {
            return;
        }

        try
        {
            var imported = ProfileInfoEditor.ImportFrom(path);
            _profileEditor.ReplaceInMemory(imported);
            ApplyProfileToUi();
            BridgeLog.Info($"profile.json importado de {path} (revisa y pulsa Guardar para aplicarlo)");
        }
        catch (Exception ex)
        {
            _shell.Dialogs.Error("Importar perfil", ex.Message);
        }
    }

    [RelayCommand]
    private async Task ExportProfileAsync()
    {
        if (_profileEditor is null)
        {
            return;
        }

        var suggested = string.IsNullOrWhiteSpace(_profileEditor.Info.Id) ? "profile" : $"{_profileEditor.Info.Id}-profile";
        var path = await _shell.Dialogs.PickSaveJsonAsync("Exportar profile.json", $"{suggested}.json");
        if (path is null)
        {
            return;
        }

        try
        {
            PullProfileFromUi();
            _profileEditor.ExportTo(path);
            BridgeLog.Info($"profile.json exportado a {path}");
        }
        catch (Exception ex)
        {
            _shell.Dialogs.Error("Exportar perfil", ex.Message);
        }
    }

    [RelayCommand]
    private async Task ImportEffectsAsync()
    {
        if (!EnsureEditable("Efectos") || _effectsEditor is null)
        {
            return;
        }

        var path = await _shell.Dialogs.PickOpenJsonAsync("Importar effects.json");
        if (path is null)
        {
            return;
        }

        try
        {
            var imported = EffectCatalogEditor.ImportFrom(path);
            _effectsEditor.ReplaceInMemory(imported);
            ApplyEffectsToUi();
            BridgeLog.Info($"effects.json importado de {path} (revisa y pulsa Guardar para aplicarlo)");
        }
        catch (Exception ex)
        {
            _shell.Dialogs.Error("Importar efectos", ex.Message);
        }
    }

    [RelayCommand]
    private async Task ExportEffectsAsync()
    {
        if (_effectsEditor is null)
        {
            return;
        }

        var suggested = string.IsNullOrWhiteSpace(_profileEditor?.Info.Id) ? "effects" : $"{_profileEditor!.Info.Id}-effects";
        var path = await _shell.Dialogs.PickSaveJsonAsync("Exportar effects.json", $"{suggested}.json");
        if (path is null)
        {
            return;
        }

        try
        {
            PullEffectsFromUi();
            _effectsEditor.ExportTo(path);
            BridgeLog.Info($"effects.json exportado a {path}");
        }
        catch (Exception ex)
        {
            _shell.Dialogs.Error("Exportar efectos", ex.Message);
        }
    }
}
