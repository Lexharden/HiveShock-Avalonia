using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using HiveShock.Avalonia.Services;
using HiveShock.Configuration;

namespace HiveShock.Avalonia.ViewModels;

public sealed partial class GiftRowViewModel : ObservableObject
{
    public GiftRowViewModel(EditableGift model)
    {
        Model = model;
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
    }

    public EditableGift Model { get; }

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _id = "";
    [ObservableProperty] private string _effect = "";
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

    partial void OnEffectChanged(string value) => Model.Effect = value;

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
}
