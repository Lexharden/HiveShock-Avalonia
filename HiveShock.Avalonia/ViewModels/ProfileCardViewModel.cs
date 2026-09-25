using CommunityToolkit.Mvvm.ComponentModel;
using HiveShock.Configuration;

namespace HiveShock.Avalonia.ViewModels;

public sealed partial class ProfileCardViewModel : ObservableObject
{
    public ProfileCardViewModel(LoadedGameProfile profile, bool selected)
    {
        Id = profile.Id;
        Title = profile.ShortDisplayName;
        Subtitle = string.IsNullOrWhiteSpace(profile.Info.Description)
            ? profile.Info.HelpNotes
            : profile.Info.Description;
        _isSelected = selected;
    }

    public string Id { get; }
    public string Title { get; }
    public string Subtitle { get; }

    [ObservableProperty] private bool _isSelected;
}
