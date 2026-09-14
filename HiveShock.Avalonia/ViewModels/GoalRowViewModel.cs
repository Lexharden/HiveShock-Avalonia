using CommunityToolkit.Mvvm.ComponentModel;

namespace HiveShock.Avalonia.ViewModels;

public sealed partial class GoalRowViewModel : ObservableObject
{
    [ObservableProperty] private string _goalId = "";
    [ObservableProperty] private string _label = "";
    [ObservableProperty] private string _needText = "2";
    [ObservableProperty] private string _effect = "";
    [ObservableProperty] private string _giftsText = "";
    [ObservableProperty] private bool _alsoInstant = true;
    [ObservableProperty] private bool _repeat = true;
    [ObservableProperty] private string _progressText = "0/2";
    [ObservableProperty] private bool _closed;
}
