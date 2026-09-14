namespace HiveShock.Avalonia.ViewModels;

public sealed class TikTokViewModel : ViewModelBase
{
    public TikTokViewModel(MainViewModel shell) => Shell = shell;

    public MainViewModel Shell { get; }
    public StudioViewModel Studio => Shell.Studio;
    public GiftsViewModel Gifts => Shell.Gifts;
    public CatalogViewModel Catalog => Shell.Catalog;
    public EventsViewModel Events => Shell.Events;
    public GoalsViewModel Goals => Shell.Goals;
}
