using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HiveShock.Configuration;

namespace HiveShock.Avalonia.ViewModels;

public sealed partial class EventsViewModel : ViewModelBase
{
    private readonly MainViewModel _shell;

    public EventsViewModel(MainViewModel shell) => _shell = shell;

    [ObservableProperty] private string _likesEveryText = "0";
    [ObservableProperty] private string _likesEffect = "";
    [ObservableProperty] private string _followEffect = "";
    [ObservableProperty] private string _shareEffect = "";
    [ObservableProperty] private bool _chatEnabled;
    [ObservableProperty] private string _chatPrefix = "!";
    [ObservableProperty] private ChatCommandRowViewModel? _selectedCommand;

    public IEnumerable<EffectChoice> EffectsWithNone =>
        new EffectChoice[] { new("", "No hacer nada") }.Concat(_shell.EffectChoices);

    public ObservableCollection<ChatCommandRowViewModel> ChatCommands { get; } = [];

    public void NotifyEffects() => OnPropertyChanged(nameof(EffectsWithNone));

    public void LoadFromEditor(GiftFileEditor editor)
    {
        LikesEveryText = editor.LikesEvery.ToString();
        LikesEffect = editor.LikesEffect;
        FollowEffect = editor.FollowEffect;
        ShareEffect = editor.ShareEffect;
        ChatEnabled = editor.ChatEnabled;
        ChatPrefix = editor.ChatPrefix;
        ChatCommands.Clear();
        foreach (var cmd in editor.ChatCommands)
        {
            ChatCommands.Add(new ChatCommandRowViewModel { Word = cmd.Word, Effect = cmd.Effect });
        }

        SelectedCommand = ChatCommands.FirstOrDefault();
    }

    public void ApplyToEditor(GiftFileEditor editor)
    {
        editor.LikesEvery = int.TryParse(LikesEveryText.Trim(), out var n) ? Math.Max(0, n) : 0;
        editor.LikesEffect = LikesEffect ?? "";
        editor.FollowEffect = FollowEffect ?? "";
        editor.ShareEffect = ShareEffect ?? "";
        editor.ChatEnabled = ChatEnabled;
        editor.ChatPrefix = string.IsNullOrWhiteSpace(ChatPrefix) ? "!" : ChatPrefix.Trim();
        editor.ChatCommands.Clear();
        foreach (var cmd in ChatCommands)
        {
            editor.ChatCommands.Add(new EditableChatCommand { Word = cmd.Word, Effect = cmd.Effect });
        }
    }

    [RelayCommand]
    private void AddCommand()
    {
        var effect = _shell.EffectChoices.FirstOrDefault(e =>
                         string.Equals(e.Id, "impulse", StringComparison.OrdinalIgnoreCase))?.Id
                     ?? _shell.EffectChoices.FirstOrDefault()?.Id
                     ?? "";
        var row = new ChatCommandRowViewModel { Word = "salto", Effect = effect };
        ChatCommands.Add(row);
        SelectedCommand = row;
    }

    [RelayCommand]
    private void RemoveCommand()
    {
        if (SelectedCommand == null)
        {
            return;
        }

        ChatCommands.Remove(SelectedCommand);
        SelectedCommand = ChatCommands.FirstOrDefault();
    }

    [RelayCommand]
    private void Save()
    {
        try
        {
            _shell.Gifts.SaveToDisk();
        }
        catch (Exception ex)
        {
            _shell.Dialogs.Error("Likes y chat", ex.Message);
        }
    }
}
