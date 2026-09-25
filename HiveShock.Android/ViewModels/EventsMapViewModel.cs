using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HiveShock.Configuration;
using HiveShock.Live;

namespace HiveShock.Android.ViewModels;

public sealed partial class EventsMapViewModel : ObservableObject
{
    private readonly MainViewModel _shell;

    public EventsMapViewModel(MainViewModel shell) => _shell = shell;

    [ObservableProperty] private string _likesEveryText = "0";
    [ObservableProperty] private string _likesEffect = "";
    [ObservableProperty] private string _followEffect = "";
    [ObservableProperty] private string _shareEffect = "";
    [ObservableProperty] private bool _chatEnabled;
    [ObservableProperty] private string _chatPrefix = "!";
    [ObservableProperty] private string _chatCooldownText = "30";
    [ObservableProperty] private string _chatGlobalGapText = "2";
    [ObservableProperty] private ChatMapRow? _selectedCommand;
    [ObservableProperty] private bool _twitchChatEnabled;
    [ObservableProperty] private string _twitchChatPrefix = "!";
    [ObservableProperty] private string _twitchChatCooldownText = "30";
    [ObservableProperty] private string _twitchChatGlobalGapText = "2";
    [ObservableProperty] private string _twitchFollowEffect = "";
    [ObservableProperty] private ChatMapRow? _selectedTwitchCommand;
    [ObservableProperty] private BitsMapRow? _selectedBitsRule;
    [ObservableProperty] private GoalMapRow? _selectedGoal;

    public bool HasGoalSelection => SelectedGoal != null;

    public IEnumerable<EffectItem> EffectsWithNone =>
        new[] { new EffectItem { Id = "", Display = "Ninguno" } }.Concat(_shell.Effects);

    public ObservableCollection<ChatMapRow> ChatCommands { get; } = [];
    public ObservableCollection<ChatMapRow> TwitchChatCommands { get; } = [];
    public ObservableCollection<BitsMapRow> TwitchBits { get; } = [];
    public ObservableCollection<GoalMapRow> Goals { get; } = [];

    public void NotifyEffects() => OnPropertyChanged(nameof(EffectsWithNone));

    partial void OnSelectedGoalChanged(GoalMapRow? value) => OnPropertyChanged(nameof(HasGoalSelection));

    public void LoadFrom(GiftFileEditor editor)
    {
        LikesEveryText = editor.LikesEvery.ToString();
        LikesEffect = editor.LikesEffect;
        FollowEffect = editor.FollowEffect;
        ShareEffect = editor.ShareEffect;
        ChatEnabled = editor.ChatEnabled;
        ChatPrefix = editor.ChatPrefix;
        ChatCooldownText = editor.ChatCooldownSec.ToString();
        ChatGlobalGapText = editor.ChatGlobalGapSec.ToString();
        ChatCommands.Clear();
        foreach (var cmd in editor.ChatCommands)
        {
            ChatCommands.Add(new ChatMapRow { Word = cmd.Word, Effect = cmd.Effect });
        }

        SelectedCommand = ChatCommands.FirstOrDefault();

        TwitchFollowEffect = editor.TwitchFollowEffect;
        TwitchChatEnabled = editor.TwitchChatEnabled;
        TwitchChatPrefix = editor.TwitchChatPrefix;
        TwitchChatCooldownText = editor.TwitchChatCooldownSec.ToString();
        TwitchChatGlobalGapText = editor.TwitchChatGlobalGapSec.ToString();
        TwitchChatCommands.Clear();
        foreach (var cmd in editor.TwitchChatCommands)
        {
            TwitchChatCommands.Add(new ChatMapRow { Word = cmd.Word, Effect = cmd.Effect });
        }

        SelectedTwitchCommand = TwitchChatCommands.FirstOrDefault();

        TwitchBits.Clear();
        foreach (var bit in editor.TwitchBits)
        {
            TwitchBits.Add(new BitsMapRow
            {
                MinText = Math.Max(1, bit.Min).ToString(),
                Effect = bit.Effect,
            });
        }

        SelectedBitsRule = TwitchBits.FirstOrDefault();

        Goals.Clear();
        foreach (var goal in editor.Goals)
        {
            Goals.Add(new GoalMapRow
            {
                GoalId = goal.Id,
                Label = goal.Label,
                NeedText = Math.Max(1, goal.Need).ToString(),
                Effect = goal.Effect,
                GiftsText = goal.GiftsText,
                AlsoInstant = goal.AlsoInstant,
                Repeat = goal.Repeat,
            });
        }

        SelectedGoal = Goals.FirstOrDefault();
        RefreshGoalProgress();
        NotifyEffects();
    }

    public void ApplyTo(GiftFileEditor editor)
    {
        editor.LikesEvery = int.TryParse(LikesEveryText.Trim(), out var n) ? Math.Max(0, n) : 0;
        editor.LikesEffect = LikesEffect ?? "";
        editor.FollowEffect = FollowEffect ?? "";
        editor.ShareEffect = ShareEffect ?? "";
        editor.ChatEnabled = ChatEnabled;
        editor.ChatPrefix = string.IsNullOrWhiteSpace(ChatPrefix) ? "!" : ChatPrefix.Trim();
        editor.ChatCooldownSec = ParseWaitSec(ChatCooldownText, 30);
        editor.ChatGlobalGapSec = ParseWaitSec(ChatGlobalGapText, 2);
        editor.ChatCommands.Clear();
        foreach (var cmd in ChatCommands)
        {
            editor.ChatCommands.Add(new EditableChatCommand { Word = cmd.Word, Effect = cmd.Effect });
        }

        editor.TwitchFollowEffect = TwitchFollowEffect ?? "";
        editor.TwitchChatEnabled = TwitchChatEnabled;
        editor.TwitchChatPrefix = string.IsNullOrWhiteSpace(TwitchChatPrefix) ? "!" : TwitchChatPrefix.Trim();
        editor.TwitchChatCooldownSec = ParseWaitSec(TwitchChatCooldownText, 30);
        editor.TwitchChatGlobalGapSec = ParseWaitSec(TwitchChatGlobalGapText, 2);
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

        editor.Goals.Clear();
        foreach (var row in Goals)
        {
            if (!int.TryParse(row.NeedText.Trim(), out var need) || need < 1)
            {
                continue;
            }

            editor.Goals.Add(new EditableGiftGoal
            {
                Id = row.GoalId ?? "",
                Label = row.Label ?? "",
                Need = need,
                Effect = row.Effect ?? "",
                GiftsText = row.GiftsText ?? "",
                AlsoInstant = row.AlsoInstant,
                Repeat = row.Repeat,
            });
        }
    }

    public void RefreshGoalProgress()
    {
        var snaps = _shell.Runtime.Goals.Snapshots();
        foreach (var row in Goals)
        {
            var snap = Match(snaps, row);
            row.ProgressText = snap?.ProgressText ?? $"0/{ParseNeed(row)}";
            if (snap != null && string.IsNullOrWhiteSpace(row.GoalId))
            {
                row.GoalId = snap.Id;
            }
        }
    }

    [RelayCommand]
    private void AddCommand() => AddChat(ChatCommands, row => SelectedCommand = row);

    [RelayCommand]
    private void RemoveCommand() =>
        RemoveChat(ChatCommands, SelectedCommand, v => SelectedCommand = v);

    [RelayCommand]
    private void AddTwitchCommand() => AddChat(TwitchChatCommands, row => SelectedTwitchCommand = row);

    [RelayCommand]
    private void RemoveTwitchCommand() =>
        RemoveChat(TwitchChatCommands, SelectedTwitchCommand, v => SelectedTwitchCommand = v);

    [RelayCommand]
    private void AddBitsRule()
    {
        var row = new BitsMapRow { MinText = "1", Effect = _shell.DefaultEffectId() };
        TwitchBits.Add(row);
        SelectedBitsRule = row;
    }

    [RelayCommand]
    private void RemoveBitsRule()
    {
        if (SelectedBitsRule is null)
        {
            return;
        }

        TwitchBits.Remove(SelectedBitsRule);
        SelectedBitsRule = TwitchBits.FirstOrDefault();
    }

    [RelayCommand]
    private void AddGoal()
    {
        var row = new GoalMapRow
        {
            Label = "Meta",
            NeedText = "2",
            Effect = _shell.DefaultEffectId(),
            GiftsText = "",
            AlsoInstant = true,
            Repeat = true,
            ProgressText = "0/2",
        };
        Goals.Add(row);
        SelectedGoal = row;
    }

    [RelayCommand]
    private void RemoveGoal()
    {
        if (SelectedGoal is null)
        {
            return;
        }

        Goals.Remove(SelectedGoal);
        SelectedGoal = Goals.FirstOrDefault();
    }

    [RelayCommand]
    private void ResetGoal()
    {
        if (SelectedGoal is null)
        {
            return;
        }

        var id = SelectedGoal.GoalId;
        if (string.IsNullOrWhiteSpace(id))
        {
            id = Match(_shell.Runtime.Goals.Snapshots(), SelectedGoal)?.Id ?? "";
        }

        if (!string.IsNullOrWhiteSpace(id))
        {
            _shell.Runtime.Goals.Reset(id);
        }

        SelectedGoal.ProgressText = $"0/{ParseNeed(SelectedGoal)}";
    }

    [RelayCommand]
    private async Task SimulateGoalAsync()
    {
        if (SelectedGoal is null)
        {
            return;
        }

        try
        {
            _shell.SaveMappings();
            RefreshGoalProgress();
            var id = SelectedGoal.GoalId;
            if (string.IsNullOrWhiteSpace(id))
            {
                id = Match(_shell.Runtime.Goals.Snapshots(), SelectedGoal)?.Id ?? "";
            }

            if (string.IsNullOrWhiteSpace(id))
            {
                _shell.Log("Guarda la meta antes de simular.");
                return;
            }

            await _shell.Runtime.SimulateGoalAsync(id).ConfigureAwait(true);
            RefreshGoalProgress();
        }
        catch (Exception ex)
        {
            _shell.Log(ex.Message);
        }
    }

    [RelayCommand]
    private void Save() => _shell.SaveMappings();

    private void AddChat(ObservableCollection<ChatMapRow> list, Action<ChatMapRow> select)
    {
        var row = new ChatMapRow { Word = "", Effect = _shell.DefaultEffectId() };
        list.Add(row);
        select(row);
    }

    private static void RemoveChat(
        ObservableCollection<ChatMapRow> list,
        ChatMapRow? selected,
        Action<ChatMapRow?> assign)
    {
        if (selected is null)
        {
            return;
        }

        list.Remove(selected);
        assign(list.FirstOrDefault());
    }

    private static int ParseWaitSec(string text, int fallback)
    {
        if (!int.TryParse((text ?? "").Trim(), out var n))
        {
            return fallback;
        }

        return Math.Clamp(n, 0, 3600);
    }

    private static GoalSnapshot? Match(IReadOnlyList<GoalSnapshot> snaps, GoalMapRow row)
    {
        if (!string.IsNullOrWhiteSpace(row.GoalId))
        {
            var byId = snaps.FirstOrDefault(s =>
                string.Equals(s.Id, row.GoalId, StringComparison.OrdinalIgnoreCase));
            if (byId != null)
            {
                return byId;
            }
        }

        if (!string.IsNullOrWhiteSpace(row.Label))
        {
            return snaps.FirstOrDefault(s =>
                string.Equals(s.Label, row.Label, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private static int ParseNeed(GoalMapRow row) =>
        int.TryParse(row.NeedText.Trim(), out var n) && n > 0 ? n : 2;
}
