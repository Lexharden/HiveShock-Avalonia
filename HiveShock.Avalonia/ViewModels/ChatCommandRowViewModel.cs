using CommunityToolkit.Mvvm.ComponentModel;

namespace HiveShock.Avalonia.ViewModels;

public sealed partial class ChatCommandRowViewModel : ViewModelBase
{
    private string _effect = "";

    [ObservableProperty] private string _word = "";

    public string Effect
    {
        get => _effect;
        set => SetEffectId(ref _effect, value);
    }
}
