using CommunityToolkit.Mvvm.ComponentModel;

namespace HiveShock.Avalonia.ViewModels;

public sealed partial class ChatCommandRowViewModel : ObservableObject
{
    [ObservableProperty] private string _word = "";
    [ObservableProperty] private string _effect = "";
}
