using CommunityToolkit.Mvvm.ComponentModel;
using HiveShock.Configuration;

namespace HiveShock.Android.ViewModels;

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
}

public sealed partial class BitsMapRow : ObservableObject
{
    [ObservableProperty] private string _minText = "1";
    [ObservableProperty] private string _effect = "";
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
}
