namespace HiveShock.Avalonia.ViewModels;

public sealed class TwitchViewModel : ViewModelBase
{
    public TwitchViewModel(MainViewModel shell) => Shell = shell;

    public MainViewModel Shell { get; }
    public StudioViewModel Studio => Shell.Studio;
    public EventsViewModel Events => Shell.Events;
}
