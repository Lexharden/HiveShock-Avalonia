using CommunityToolkit.Mvvm.ComponentModel;
using HiveShock.Configuration;

namespace HiveShock.Mobile.ViewModels;

public sealed partial class GiftMapRow : ObservableObject
{
    public GiftMapRow(EditableGift model)
    {
        Model = model;
        _name = model.Gift;
        _id = model.Id;
        _effect = model.Effect;
        _note = model.What;
        _minCount = Math.Max(1, model.MinCount);
        _each = model.Each;
        _also = string.Join(", ", model.Also);
        _diamonds = model.Diamonds;
    }

    public EditableGift Model { get; }

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _id = "";
    [ObservableProperty] private string _effect = "";
    [ObservableProperty] private string _note = "";
    [ObservableProperty] private int _minCount = 1;
    [ObservableProperty] private bool _each;
    [ObservableProperty] private string _also = "";
    [ObservableProperty] private int? _diamonds;

    partial void OnNameChanged(string value) => Model.Gift = value;

    partial void OnIdChanged(string value) => Model.Id = value;

    partial void OnEffectChanged(string value) => Model.Effect = value;

    partial void OnNoteChanged(string value) => Model.What = value;

    partial void OnMinCountChanged(int value) => Model.MinCount = Math.Max(1, value);

    partial void OnEachChanged(bool value) => Model.Each = value;

    partial void OnDiamondsChanged(int? value) => Model.Diamonds = value;

    partial void OnAlsoChanged(string value) =>
        Model.Also = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
}

public sealed partial class ChatMapRow : ObservableObject
{
    [ObservableProperty] private string _word = "";
    [ObservableProperty] private string _effect = "";

    /// <summary>Efectos adicionales (se configuran en el escritorio): aquí no se editan, pero se conservan al guardar.</summary>
    public List<HiveShock.Configuration.EditableGiftStep> MoreSteps { get; init; } = [];
}

public sealed partial class BitsMapRow : ObservableObject
{
    [ObservableProperty] private string _minText = "1";
    [ObservableProperty] private string _effect = "";

    /// <summary>Efectos adicionales (se configuran en el escritorio): aquí no se editan, pero se conservan al guardar.</summary>
    public List<HiveShock.Configuration.EditableGiftStep> MoreSteps { get; init; } = [];
}

/// <summary>Recompensa de puntos del canal de Twitch → efecto.</summary>
public sealed partial class RewardMapRow : ObservableObject
{
    private string _rewardId = "";

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _effect = "";

    /// <summary>El desplegable manda null al rehacer su lista: no es una elección y se ignora.</summary>
    public string RewardId
    {
        get => _rewardId;
        set
        {
            if (value is null)
            {
                OnPropertyChanged();
                return;
            }

            SetProperty(ref _rewardId, value);
        }
    }

    /// <summary>
    /// Efectos adicionales configurados en el escritorio. Aquí no se editan, pero se conservan al guardar
    /// para no borrarlos.
    /// </summary>
    public List<HiveShock.Configuration.EditableGiftStep> MoreSteps { get; init; } = [];

    public void RepaintSelection() => OnPropertyChanged(nameof(RewardId));
}

/// <summary>Una recompensa del canal en el desplegable.</summary>
public sealed record RewardOption(string Id, string Title, int Cost)
{
    public string Display => $"{Title} · {Cost:N0} puntos";
}

public sealed partial class GoalMapRow : ObservableObject
{
    [ObservableProperty] private string _goalId = "";
    [ObservableProperty] private string _label = "";
    [ObservableProperty] private string _needText = "2";
    [ObservableProperty] private string _effect = "";
    [ObservableProperty] private string _giftsText = "";
    [ObservableProperty] private bool _alsoInstant = true;
    [ObservableProperty] private bool _repeat = true;
    [ObservableProperty] private string _progressText = "0/2";

    /// <summary>Efectos adicionales (se configuran en el escritorio): aquí no se editan, pero se conservan al guardar.</summary>
    public List<HiveShock.Configuration.EditableGiftStep> MoreSteps { get; init; } = [];
}
