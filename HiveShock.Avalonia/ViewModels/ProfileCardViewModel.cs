using CommunityToolkit.Mvvm.ComponentModel;
using HiveShock.Configuration;

namespace HiveShock.Avalonia.ViewModels;

public sealed partial class ProfileCardViewModel : ObservableObject
{
    public ProfileCardViewModel(LoadedGameProfile profile, bool selected)
    {
        Id = profile.Id;
        Title = ShortTitle(profile);
        Subtitle = profile.Info.HelpNotes;
        if (string.IsNullOrWhiteSpace(Subtitle))
        {
            Subtitle = profile.Info.Description;
        }

        _isSelected = selected;
    }

    public string Id { get; }
    public string Title { get; }
    public string Subtitle { get; }

    [ObservableProperty] private bool _isSelected;

    private static string ShortTitle(LoadedGameProfile profile)
    {
        var name = profile.DisplayName;
        var paren = name.IndexOf('(');
        return paren > 0 ? name[..paren].Trim() : name;
    }
}
