using CommunityToolkit.Mvvm.ComponentModel;

namespace HiveShock.Avalonia.ViewModels;

public sealed partial class TikTokViewModel : ViewModelBase
{
    public TikTokViewModel(MainViewModel shell)
    {
        Shell = shell;
        AccountOpen = string.IsNullOrWhiteSpace(shell.Studio.Channel);
#if DEBUG
        Diagnostics = new DiagnosticViewModel(shell.Runtime.Diagnostics);
#endif
    }

    public MainViewModel Shell { get; }
    public StudioViewModel Studio => Shell.Studio;
    public GiftsViewModel Gifts => Shell.Gifts;
    public CatalogViewModel Catalog => Shell.Catalog;
    public EventsViewModel Events => Shell.Events;
    public GoalsViewModel Goals => Shell.Goals;
    public ProfileEditorViewModel ProfileEditor => Shell.ProfileEditor;

    [ObservableProperty] private bool _accountOpen;

    /// <summary>Returns true only in DEBUG builds; used to show/hide the Debug tab in AXAML.</summary>
    public bool IsDebugBuild =>
#if DEBUG
        true;
#else
        false;
#endif

#if DEBUG
    public DiagnosticViewModel? Diagnostics { get; }
#else
    public DiagnosticViewModel? Diagnostics => null;
#endif
}
