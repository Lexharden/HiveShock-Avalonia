using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HiveShock.Configuration;

namespace HiveShock.Avalonia.ViewModels;

/// <summary>Un efecto adicional de un regalo: qué efecto, sus parámetros y cuánto espera antes de salir.</summary>
public sealed partial class GiftStepRowViewModel : ViewModelBase
{
    private readonly EffectCatalog _effects;
    private readonly Action _changed;
    private readonly Action<GiftStepRowViewModel> _remove;
    private string _effect;

    public GiftStepRowViewModel(
        EditableGiftStep model,
        EffectCatalog effects,
        ObservableCollection<EffectChoice> choices,
        Action changed,
        Action<GiftStepRowViewModel> remove)
    {
        Model = model;
        _effects = effects;
        EffectChoices = choices;
        _changed = changed;
        _remove = remove;
        _effect = model.Effect;
        _delayText = model.DelaySec > 0 ? model.DelaySec.ToString("0.##", CultureInfo.InvariantCulture) : "";
        RebuildParamFields();
    }

    public EditableGiftStep Model { get; }

    public ObservableCollection<EffectChoice> EffectChoices { get; }

    public ObservableCollection<EffectParamFieldViewModel> ParamFields { get; } = [];

    public bool HasParams => ParamFields.Count > 0;

    public string Effect
    {
        get => _effect;
        set
        {
            if (SetEffectId(ref _effect, value))
            {
                Model.Effect = value;
                RebuildParamFields();
                _changed();
            }
        }
    }

    [ObservableProperty] private string _delayText;

    partial void OnDelayTextChanged(string value)
    {
        var text = value.Trim().Replace(',', '.');
        Model.DelaySec = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? Math.Clamp(seconds, 0, EffectPlan.MaxDelaySec)
            : 0;
        _changed();
    }

    [RelayCommand]
    private void Remove() => _remove(this);

    private void RebuildParamFields()
    {
        var schema = _effects.GetParamSchema(Effect);
        var allowed = new HashSet<string>(schema.Select(p => p.Key), StringComparer.OrdinalIgnoreCase);
        foreach (var key in Model.Params.Keys.Where(k => !allowed.Contains(k)).ToList())
        {
            Model.Params.Remove(key);
        }

        ParamFields.Clear();
        foreach (var def in schema)
        {
            var field = new EffectParamFieldViewModel(Model.Params, def);
            field.PropertyChanged += (_, _) => _changed();
            ParamFields.Add(field);
        }

        OnPropertyChanged(nameof(HasParams));
    }
}
