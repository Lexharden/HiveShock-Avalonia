using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HiveShock.Avalonia.Services;
using HiveShock.Configuration;

namespace HiveShock.Avalonia.ViewModels;

public sealed partial class GiftRowViewModel : ViewModelBase
{
    private readonly EffectCatalog _effects;
    private readonly ObservableCollection<EffectChoice> _choices;

    public GiftRowViewModel(EditableGift model, EffectCatalog effects, ObservableCollection<EffectChoice>? choices = null)
    {
        Model = model;
        _effects = effects;
        _choices = choices ?? [];
        _name = model.Gift;
        _id = model.Id;
        _effect = model.Effect;
        _note = model.What;
        _minCount = Math.Max(1, model.MinCount);
        _each = model.Each;
        _overlay = model.Overlay;
        _overlayText = model.OverlayText;
        _diamonds = model.Diamonds;
        _image = model.Image;
        _also = string.Join(", ", model.Also);
        RebuildParamFields();
        foreach (var step in model.MoreSteps)
        {
            ExtraSteps.Add(NewStepRow(step));
        }
    }

    public EditableGift Model { get; }

    /// <summary>Efectos que se lanzan después del principal, en orden.</summary>
    public ObservableCollection<GiftStepRowViewModel> ExtraSteps { get; } = [];

    public bool HasExtraSteps => ExtraSteps.Count > 0;

    public bool CanAddStep => ExtraSteps.Count < EffectPlan.MaxSteps - 1;

    /// <summary>«3 efectos» para la lista de regalos; vacío si solo hace una cosa.</summary>
    public string StepSummary => ExtraSteps.Count == 0 ? "" : $"{ExtraSteps.Count + 1} efectos";

    [RelayCommand(CanExecute = nameof(CanAddStep))]
    private void AddStep()
    {
        var effect = _choices.FirstOrDefault(e => string.Equals(e.Id, "impulse", StringComparison.OrdinalIgnoreCase))?.Id
                     ?? _choices.FirstOrDefault()?.Id
                     ?? "";
        var model = new EditableGiftStep { Effect = effect };
        Model.MoreSteps.Add(model);
        ExtraSteps.Add(NewStepRow(model));
        StepsChanged();
    }

    private GiftStepRowViewModel NewStepRow(EditableGiftStep step) =>
        new(step, _effects, _choices, StepsEdited, RemoveStep);

    private void RemoveStep(GiftStepRowViewModel step)
    {
        Model.MoreSteps.Remove(step.Model);
        ExtraSteps.Remove(step);
        StepsChanged();
    }

    private void StepsChanged()
    {
        OnPropertyChanged(nameof(HasExtraSteps));
        OnPropertyChanged(nameof(CanAddStep));
        OnPropertyChanged(nameof(StepSummary));
        AddStepCommand.NotifyCanExecuteChanged();
        StepsEdited();
    }

    /// <summary>Se editó algún efecto adicional: avisa para el guardado automático.</summary>
    private void StepsEdited() => OnPropertyChanged(nameof(ParamsEdited));

    public ObservableCollection<EffectParamFieldViewModel> ParamFields { get; } = [];

    public bool HasParams => ParamFields.Count > 0;

    /// <summary>Solo avisa de que se editó un parámetro del efecto (para el guardado automático).</summary>
    public int ParamsEdited => 0;

    public bool ShowInstaKillHint =>
        string.Equals(Effect, "insta_kill", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Effect, "instakill", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Effect, "kill", StringComparison.OrdinalIgnoreCase);

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _id = "";
    private string _effect = "";

    public string Effect
    {
        get => _effect;
        set
        {
            if (SetEffectId(ref _effect, value))
            {
                Model.Effect = value;
                RebuildParamFields();
                OnPropertyChanged(nameof(ShowInstaKillHint));
            }
        }
    }
    [ObservableProperty] private string _note = "";
    [ObservableProperty] private int _minCount = 1;
    [ObservableProperty] private bool _each;
    [ObservableProperty] private bool _overlay;
    [ObservableProperty] private string _overlayText = "";
    [ObservableProperty] private int? _diamonds;
    [ObservableProperty] private string _image = "";
    [ObservableProperty] private string _also = "";

    public Bitmap? Thumb => GiftImageLoader.Load(Model.ResolvedImagePath);

    public string EffectLabel { get; set; } = "";

    partial void OnNameChanged(string value)
    {
        Model.Gift = value;
        OnPropertyChanged(nameof(Thumb));
    }

    partial void OnIdChanged(string value)
    {
        Model.Id = value;
        OnPropertyChanged(nameof(Thumb));
    }

    partial void OnNoteChanged(string value) => Model.What = value;

    partial void OnMinCountChanged(int value) => Model.MinCount = Math.Max(1, value);

    partial void OnEachChanged(bool value) => Model.Each = value;

    partial void OnOverlayChanged(bool value) => Model.Overlay = value;

    partial void OnOverlayTextChanged(string value) => Model.OverlayText = value;

    partial void OnDiamondsChanged(int? value) => Model.Diamonds = value;

    partial void OnImageChanged(string value)
    {
        Model.Image = value;
        OnPropertyChanged(nameof(Thumb));
    }

    partial void OnAlsoChanged(string value)
    {
        Model.Also = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        OnPropertyChanged(nameof(Thumb));
    }

    public void RefreshThumb() => OnPropertyChanged(nameof(Thumb));

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
            var field = new EffectParamFieldViewModel(Model, def);
            field.PropertyChanged += (_, _) => OnPropertyChanged(nameof(ParamsEdited));
            ParamFields.Add(field);
        }

        OnPropertyChanged(nameof(HasParams));
    }
}
