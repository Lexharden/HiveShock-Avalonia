using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using HiveShock.Avalonia.Services;
using HiveShock.Configuration;

namespace HiveShock.Avalonia.ViewModels;

public sealed partial class GiftRowViewModel : ObservableObject
{
    private readonly EffectCatalog _effects;

    public GiftRowViewModel(EditableGift model, EffectCatalog effects)
    {
        Model = model;
        _effects = effects;
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
    }

    public EditableGift Model { get; }

    public ObservableCollection<EffectParamFieldViewModel> ParamFields { get; } = [];

    public bool HasParams => ParamFields.Count > 0;

    public bool ShowInstaKillHint =>
        string.Equals(Effect, "insta_kill", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Effect, "instakill", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Effect, "kill", StringComparison.OrdinalIgnoreCase);

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

    partial void OnEffectChanged(string value)
    {
        Model.Effect = value;
        RebuildParamFields();
        OnPropertyChanged(nameof(ShowInstaKillHint));
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
            ParamFields.Add(new EffectParamFieldViewModel(Model, def));
        }

        OnPropertyChanged(nameof(HasParams));
    }
}
