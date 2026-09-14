using CommunityToolkit.Mvvm.ComponentModel;

namespace HiveShock.Avalonia.ViewModels;

public sealed partial class TwitchViewModel : ViewModelBase
{
    public TwitchViewModel(MainViewModel shell)
    {
        Shell = shell;
        AccountOpen = !shell.Studio.TwitchHasAccount;
    }

    public MainViewModel Shell { get; }
    public StudioViewModel Studio => Shell.Studio;
    public EventsViewModel Events => Shell.Events;

    [ObservableProperty] private bool _accountOpen;
}
