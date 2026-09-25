using CommunityToolkit.Mvvm.ComponentModel;

namespace HiveShock.Avalonia.ViewModels;

public sealed partial class BitsRowViewModel : ObservableObject
{
    [ObservableProperty] private string _minText = "1";
    [ObservableProperty] private string _effect = "";
}
