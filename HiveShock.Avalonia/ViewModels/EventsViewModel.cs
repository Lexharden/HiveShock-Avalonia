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
    [ObservableProperty] private bool _twitchChatEnabled;
    [ObservableProperty] private string _twitchChatPrefix = "!";
    [ObservableProperty] private string _twitchFollowEffect = "";
    [ObservableProperty] private ChatCommandRowViewModel? _selectedTwitchCommand;
    [ObservableProperty] private BitsRowViewModel? _selectedBitsRule;

    public IEnumerable<EffectChoice> EffectsWithNone =>
        new EffectChoice[] { new("", "Ninguno") }.Concat(_shell.EffectChoices);

    public ObservableCollection<ChatCommandRowViewModel> ChatCommands { get; } = [];
    public ObservableCollection<ChatCommandRowViewModel> TwitchChatCommands { get; } = [];
    public ObservableCollection<BitsRowViewModel> TwitchBits { get; } = [];

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

        TwitchFollowEffect = editor.TwitchFollowEffect;
        TwitchChatEnabled = editor.TwitchChatEnabled;
        TwitchChatPrefix = editor.TwitchChatPrefix;
        TwitchChatCommands.Clear();
        foreach (var cmd in editor.TwitchChatCommands)
        {
            TwitchChatCommands.Add(new ChatCommandRowViewModel { Word = cmd.Word, Effect = cmd.Effect });
        }

        SelectedTwitchCommand = TwitchChatCommands.FirstOrDefault();

        TwitchBits.Clear();
        foreach (var bit in editor.TwitchBits)
        {
            TwitchBits.Add(new BitsRowViewModel
            {
                MinText = Math.Max(1, bit.Min).ToString(),
                Effect = bit.Effect,
            });
        }

        SelectedBitsRule = TwitchBits.FirstOrDefault();
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

        editor.TwitchFollowEffect = TwitchFollowEffect ?? "";
        editor.TwitchChatEnabled = TwitchChatEnabled;
        editor.TwitchChatPrefix = string.IsNullOrWhiteSpace(TwitchChatPrefix) ? "!" : TwitchChatPrefix.Trim();
        editor.TwitchChatCommands.Clear();
        foreach (var cmd in TwitchChatCommands)
        {
            editor.TwitchChatCommands.Add(new EditableChatCommand { Word = cmd.Word, Effect = cmd.Effect });
        }

        editor.TwitchBits.Clear();
        foreach (var bit in TwitchBits)
        {
            if (!int.TryParse(bit.MinText.Trim(), out var min) || min < 1)
            {
                continue;
            }

            editor.TwitchBits.Add(new EditableBitsRule { Min = min, Effect = bit.Effect ?? "" });
        }
    }

    [RelayCommand]
    private void AddCommand() => AddTo(ChatCommands, cmd => SelectedCommand = cmd);

    [RelayCommand]
    private void RemoveCommand() => RemoveFrom(ChatCommands, SelectedCommand, v => SelectedCommand = v);

    [RelayCommand]
    private void AddTwitchCommand() => AddTo(TwitchChatCommands, cmd => SelectedTwitchCommand = cmd);

    [RelayCommand]
    private void RemoveTwitchCommand() =>
        RemoveFrom(TwitchChatCommands, SelectedTwitchCommand, v => SelectedTwitchCommand = v);

    [RelayCommand]
    private void AddBitsRule()
    {
        var effect = _shell.EffectChoices.FirstOrDefault(e =>
                         string.Equals(e.Id, "impulse", StringComparison.OrdinalIgnoreCase))?.Id
                     ?? _shell.EffectChoices.FirstOrDefault()?.Id
                     ?? "";
        var row = new BitsRowViewModel { MinText = "1", Effect = effect };
        TwitchBits.Add(row);
        SelectedBitsRule = row;
    }

    [RelayCommand]
    private void RemoveBitsRule()
    {
        if (SelectedBitsRule == null)
        {
            return;
        }

        TwitchBits.Remove(SelectedBitsRule);
        SelectedBitsRule = TwitchBits.FirstOrDefault();
    }

    private void AddTo(
        ObservableCollection<ChatCommandRowViewModel> list,
        Action<ChatCommandRowViewModel> select)
    {
        var effect = _shell.EffectChoices.FirstOrDefault(e =>
                         string.Equals(e.Id, "impulse", StringComparison.OrdinalIgnoreCase))?.Id
                     ?? _shell.EffectChoices.FirstOrDefault()?.Id
                     ?? "";
        var row = new ChatCommandRowViewModel { Word = "salto", Effect = effect };
        list.Add(row);
        select(row);
    }

    private static void RemoveFrom(
        ObservableCollection<ChatCommandRowViewModel> list,
        ChatCommandRowViewModel? selected,
        Action<ChatCommandRowViewModel?> assign)
    {
        if (selected == null)
        {
            return;
        }

        list.Remove(selected);
        assign(list.FirstOrDefault());
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
            _shell.Dialogs.Error("Eventos", ex.Message);
        }
    }
}
