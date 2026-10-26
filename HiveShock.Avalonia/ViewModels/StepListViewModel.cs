using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HiveShock.Configuration;

namespace HiveShock.Avalonia.ViewModels;

/// <summary>
/// «Más efectos» de un disparador (comando de chat, seguir, bits, meta…): los que se lanzan después del principal,
/// en orden y con una espera opcional. Cualquier cambio avisa con <see cref="EditedProperty"/> para el guardado automático.
/// </summary>
public sealed partial class StepListViewModel : ViewModelBase
{
    public const string EditedProperty = "StepsEdited";

    private readonly Func<EffectCatalog> _effects;
    private readonly ObservableCollection<EffectChoice> _choices;

    public StepListViewModel(Func<EffectCatalog> effects, ObservableCollection<EffectChoice> choices)
    {
        _effects = effects;
        _choices = choices;
    }

    /// <summary>Los pasos tal como se guardarán (los que se editan en <see cref="Rows"/>).</summary>
    public List<EditableGiftStep> Model { get; } = [];

    public ObservableCollection<GiftStepRowViewModel> Rows { get; } = [];

    public bool HasSteps => Rows.Count > 0;

    public bool CanAdd => Rows.Count < EffectPlan.MaxSteps - 1;

    /// <summary>«+2 efectos» para listas; vacío si no hay extras.</summary>
    public string Summary => Rows.Count == 0 ? "" : Rows.Count == 1 ? "+1 efecto" : $"+{Rows.Count} efectos";

    /// <summary>Sustituye los pasos por una copia de <paramref name="steps"/> (sin tocar los originales).</summary>
    public void Load(IEnumerable<EditableGiftStep> steps)
    {
        Model.Clear();
        Rows.Clear();
        foreach (var step in steps)
        {
            var copy = new EditableGiftStep
            {
                Effect = step.Effect,
                DelaySec = step.DelaySec,
                Params = new Dictionary<string, double>(step.Params, StringComparer.OrdinalIgnoreCase),
            };
            Model.Add(copy);
            Rows.Add(NewRow(copy));
        }

        Refresh();
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add()
    {
        var effect = _choices.FirstOrDefault(e => string.Equals(e.Id, "impulse", StringComparison.OrdinalIgnoreCase))?.Id
                     ?? _choices.FirstOrDefault()?.Id
                     ?? "";
        var model = new EditableGiftStep { Effect = effect };
        Model.Add(model);
        Rows.Add(NewRow(model));
        Changed();
    }

    private GiftStepRowViewModel NewRow(EditableGiftStep step) =>
        new(step, _effects(), _choices, Edited, Remove);

    private void Remove(GiftStepRowViewModel row)
    {
        Model.Remove(row.Model);
        Rows.Remove(row);
        Changed();
    }

    private void Changed()
    {
        Refresh();
        Edited();
    }

    private void Edited() => OnPropertyChanged(EditedProperty);

    private void Refresh()
    {
        OnPropertyChanged(nameof(HasSteps));
        OnPropertyChanged(nameof(CanAdd));
        OnPropertyChanged(nameof(Summary));
        AddCommand.NotifyCanExecuteChanged();
    }
}
