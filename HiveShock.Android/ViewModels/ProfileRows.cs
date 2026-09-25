using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HiveShock.Configuration;

namespace HiveShock.Android.ViewModels;

public sealed partial class ProfileLabelRow : ObservableObject
{
    public ProfileLabelRow(EditableLabel model)
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

public sealed partial class ProfileEffectPropertyRow : ObservableObject
{
    public static readonly string[] KindOptions = ["Texto", "Número", "Sí / No"];

    public ProfileEffectPropertyRow(EditableEffectProperty model)
    {
        Model = model;
        _key = model.Key;
        _value = model.Value;
        _kindText = KindToText(model.Kind);
    }

    public EditableEffectProperty Model { get; }

    [ObservableProperty] private string _key = "";
    [ObservableProperty] private string _value = "";
    [ObservableProperty] private string _kindText = "Texto";

    partial void OnKeyChanged(string value) => Model.Key = value;
    partial void OnValueChanged(string value) => Model.Value = value;
    partial void OnKindTextChanged(string value) => Model.Kind = TextToKind(value);

    private static string KindToText(EditableValueKind kind) => kind switch
    {
        EditableValueKind.Number => "Número",
        EditableValueKind.Bool => "Sí / No",
        _ => "Texto",
    };

    private static EditableValueKind TextToKind(string text) => text switch
    {
        "Número" => EditableValueKind.Number,
        "Sí / No" => EditableValueKind.Bool,
        _ => EditableValueKind.Text,
    };
}

public sealed partial class ProfileEffectRow : ObservableObject
{
    public ProfileEffectRow(EditableEffect model)
    {
        Model = model;
        _id = model.Id;
    }

    public EditableEffect Model { get; }

    [ObservableProperty] private string _id = "";
    [ObservableProperty] private bool _isEnemy;
    [ObservableProperty] private bool _isSingleton;

    public ObservableCollection<ProfileEffectPropertyRow> Properties { get; } = [];

    partial void OnIdChanged(string value) => Model.Id = value;
}
