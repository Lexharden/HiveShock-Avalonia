using CommunityToolkit.Mvvm.ComponentModel;

namespace HiveShock.Avalonia.ViewModels;

public sealed partial class BitsRowViewModel : ViewModelBase
{
    private string _effect = "";

    [ObservableProperty] private string _minText = "1";

    public string Effect
    {
        get => _effect;
        set => SetEffectId(ref _effect, value);
    }
}
