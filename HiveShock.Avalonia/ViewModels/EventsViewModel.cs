using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HiveShock.Configuration;
using HiveShock.Live;
using HiveShock.Logging;

namespace HiveShock.Avalonia.ViewModels;

public sealed partial class EventsViewModel : ViewModelBase
{
    private readonly MainViewModel _shell;

    public EventsViewModel(MainViewModel shell)
    {
        _shell = shell;
        // Todo lo editable de esta página cuenta; solo se ignora lo que es selección de la lista.
        static bool Ignore(string? name) =>
            name != null && (name.StartsWith("Selected", StringComparison.Ordinal) || name == nameof(EffectsWithNone) ||
                             name.StartsWith("ChannelRewards", StringComparison.Ordinal));
        LikesSteps = NewSteps();
        FollowSteps = NewSteps();
        ShareSteps = NewSteps();
        TwitchFollowSteps = NewSteps();
        var saver = shell.Gifts.AutoSaver;
        saver.Track(LikesSteps);
        saver.Track(FollowSteps);
        saver.Track(ShareSteps);
        saver.Track(TwitchFollowSteps);
        saver.Track(this, Ignore);
        saver.Track(ChatCommands);
        saver.Track(TwitchChatCommands);
        saver.Track(TwitchBits);
        // El coste viene de la lista del canal y no se guarda: refrescarla no es un cambio del usuario.
        saver.Track(TwitchRewards, name => name is nameof(RewardRowViewModel.Cost) or nameof(RewardRowViewModel.CostText));
    }

    public SaveStatusViewModel SaveStatus => _shell.Gifts.SaveStatus;

    /// <summary>Efectos adicionales de likes, seguir, compartir y seguir en Twitch.</summary>
    public StepListViewModel LikesSteps { get; }
    public StepListViewModel FollowSteps { get; }
    public StepListViewModel ShareSteps { get; }
    public StepListViewModel TwitchFollowSteps { get; }

    private StepListViewModel NewSteps() => new(() => _shell.Runtime.Effects, _shell.EffectChoices);

    private StepListViewModel NewSteps(IEnumerable<EditableGiftStep> steps)
    {
        var list = NewSteps();
        list.Load(steps);
        return list;
    }

    [ObservableProperty] private string _likesEveryText = "0";
    private string _likesEffect = "";

    public string LikesEffect
    {
        get => _likesEffect;
        set => SetEffectId(ref _likesEffect, value);
    }
    private string _followEffect = "";

    public string FollowEffect
    {
        get => _followEffect;
        set => SetEffectId(ref _followEffect, value);
    }
    [ObservableProperty] private bool _followOncePerUser = true;
    private string _shareEffect = "";

    public string ShareEffect
    {
        get => _shareEffect;
        set => SetEffectId(ref _shareEffect, value);
    }
    [ObservableProperty] private bool _chatEnabled;
    [ObservableProperty] private string _chatPrefix = "!";
    [ObservableProperty] private string _chatCooldownText = "30";
    [ObservableProperty] private string _chatGlobalGapText = "2";
    [ObservableProperty] private ChatCommandRowViewModel? _selectedCommand;
    [ObservableProperty] private bool _twitchChatEnabled;
    [ObservableProperty] private string _twitchChatPrefix = "!";
    [ObservableProperty] private string _twitchChatCooldownText = "30";
    [ObservableProperty] private string _twitchChatGlobalGapText = "2";
    private string _twitchFollowEffect = "";

    public string TwitchFollowEffect
    {
        get => _twitchFollowEffect;
        set => SetEffectId(ref _twitchFollowEffect, value);
    }
    [ObservableProperty] private ChatCommandRowViewModel? _selectedTwitchCommand;
    [ObservableProperty] private BitsRowViewModel? _selectedBitsRule;
    [ObservableProperty] private RewardRowViewModel? _selectedReward;

    // Lista de recompensas del canal (Twitch): no es configuración del usuario, el guardado la ignora (ChannelRewards*).
    [ObservableProperty] private string _channelRewardsStatus = "";
    [ObservableProperty] private bool _channelRewardsBusy;
    [ObservableProperty] private bool _channelRewardsNeedRelogin;
    private bool _channelRewardsLoaded;

    public IEnumerable<EffectChoice> EffectsWithNone =>
        new EffectChoice[] { new("", "Ninguno") }.Concat(_shell.EffectChoices);

    public ObservableCollection<ChatCommandRowViewModel> ChatCommands { get; } = [];
    public ObservableCollection<ChatCommandRowViewModel> TwitchChatCommands { get; } = [];
    public ObservableCollection<BitsRowViewModel> TwitchBits { get; } = [];
    public ObservableCollection<RewardRowViewModel> TwitchRewards { get; } = [];

    /// <summary>Recompensas reales del canal para el desplegable (vacía hasta leerlas de Twitch).</summary>
    public ObservableCollection<RewardChoice> ChannelRewards { get; } = [];

    public bool ChannelRewardsHasStatus => ChannelRewardsStatus.Length > 0;

    // Selección múltiple de cada lista (Ctrl/Mayús+clic): Supr o la papelera los quitan juntos.
    public ObservableCollection<ChatCommandRowViewModel> SelectedChatCommands { get; } = [];
    public ObservableCollection<ChatCommandRowViewModel> SelectedTwitchCommands { get; } = [];
    public ObservableCollection<BitsRowViewModel> SelectedBitsRules { get; } = [];
    public ObservableCollection<RewardRowViewModel> SelectedRewards { get; } = [];

    public void NotifyEffects() => OnPropertyChanged(nameof(EffectsWithNone));

    [ObservableProperty] private string _brokenText = "";

    public bool HasBroken => BrokenText.Length > 0;

    partial void OnBrokenTextChanged(string value) => OnPropertyChanged(nameof(HasBroken));

    /// <summary>
    /// Avisa de comandos y reglas cuyo efecto ya no existe (p. ej. se borró la macro a la que apuntaban):
    /// en vivo no harían nada y nadie lo notaría.
    /// </summary>
    public void RefreshBroken()
    {
        var known = _shell.EffectChoices.Select(e => e.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool Missing(string? effect) => !string.IsNullOrWhiteSpace(effect) && !known.Contains(effect.Trim());

        var broken = new List<string>();
        IEnumerable<string> MissingSteps(StepListViewModel? steps) =>
            steps?.Model.Select(s => s.Effect).Where(Missing) ?? [];
        broken.AddRange(ChatCommands.SelectMany(c => new[] { c.Effect }.Concat(MissingSteps(c.Steps)).Where(Missing)
            .Select(e => $"{ChatPrefix}{c.Word} → {e}")));
        broken.AddRange(TwitchChatCommands.SelectMany(c => new[] { c.Effect }.Concat(MissingSteps(c.Steps)).Where(Missing)
            .Select(e => $"Twitch {TwitchChatPrefix}{c.Word} → {e}")));
        broken.AddRange(TwitchBits.SelectMany(b => new[] { b.Effect }.Concat(MissingSteps(b.Steps)).Where(Missing)
            .Select(e => $"Bits {b.MinText} → {e}")));
        broken.AddRange(TwitchRewards.SelectMany(r => new[] { r.Effect }.Concat(MissingSteps(r.Steps)).Where(Missing)
            .Select(e => $"Recompensa «{r.DisplayTitle}» → {e}")));
        foreach (var (what, steps) in new[]
                 {
                     ("Likes", LikesSteps), ("Seguir", FollowSteps), ("Compartir", ShareSteps),
                     ("Seguir en Twitch", TwitchFollowSteps),
                 })
        {
            broken.AddRange(MissingSteps(steps).Select(e => $"{what} → {e}"));
        }

        foreach (var (what, effect) in new[]
                 {
                     ("Likes", LikesEffect), ("Seguir", FollowEffect), ("Compartir", ShareEffect),
                     ("Seguir en Twitch", TwitchFollowEffect),
                 })
        {
            if (Missing(effect))
            {
                broken.Add($"{what} → {effect}");
            }
        }

        BrokenText = broken.Count == 0
            ? ""
            : "Sin efecto (la macro o el efecto ya no existe): " + string.Join(" · ", broken);
    }

    public void LoadFromEditor(GiftFileEditor editor)
    {
        // Lo que se ofrecía deshacer eran filas de antes de recargar: ya no valen.
        _shell.Undo.Dismiss();
        using var _ = _shell.Gifts.AutoSaver.Suppress();
        LikesEveryText = editor.LikesEvery.ToString();
        LikesEffect = editor.LikesEffect;
        LikesSteps.Load(editor.LikesMore);
        FollowEffect = editor.FollowEffect;
        FollowSteps.Load(editor.FollowMore);
        FollowOncePerUser = editor.FollowOncePerUser;
        ShareEffect = editor.ShareEffect;
        ShareSteps.Load(editor.ShareMore);
        ChatEnabled = editor.ChatEnabled;
        ChatPrefix = editor.ChatPrefix;
        ChatCooldownText = editor.ChatCooldownSec.ToString();
        ChatGlobalGapText = editor.ChatGlobalGapSec.ToString();
        ChatCommands.Clear();
        foreach (var cmd in editor.ChatCommands)
        {
            var row = new ChatCommandRowViewModel { Word = cmd.Word, Effect = cmd.Effect };
            row.AttachSteps(NewSteps(cmd.MoreSteps));
            ChatCommands.Add(row);
        }

        SelectedCommand = ChatCommands.FirstOrDefault();

        TwitchFollowEffect = editor.TwitchFollowEffect;
        TwitchFollowSteps.Load(editor.TwitchFollowMore);
        TwitchChatEnabled = editor.TwitchChatEnabled;
        TwitchChatPrefix = editor.TwitchChatPrefix;
        TwitchChatCooldownText = editor.TwitchChatCooldownSec.ToString();
        TwitchChatGlobalGapText = editor.TwitchChatGlobalGapSec.ToString();
        TwitchChatCommands.Clear();
        foreach (var cmd in editor.TwitchChatCommands)
        {
            var row = new ChatCommandRowViewModel { Word = cmd.Word, Effect = cmd.Effect };
            row.AttachSteps(NewSteps(cmd.MoreSteps));
            TwitchChatCommands.Add(row);
        }

        SelectedTwitchCommand = TwitchChatCommands.FirstOrDefault();

        TwitchBits.Clear();
        foreach (var bit in editor.TwitchBits)
        {
            var row = new BitsRowViewModel
            {
                MinText = Math.Max(1, bit.Min).ToString(),
                Effect = bit.Effect,
            };
            row.AttachSteps(NewSteps(bit.MoreSteps));
            TwitchBits.Add(row);
        }

        SelectedBitsRule = TwitchBits.FirstOrDefault();

        TwitchRewards.Clear();
        foreach (var reward in editor.TwitchRewards)
        {
            var row = NewRewardRow(reward.RewardId, reward.Title, reward.Effect);
            row.AttachSteps(NewSteps(reward.MoreSteps));
            TwitchRewards.Add(row);
        }

        SelectedReward = TwitchRewards.FirstOrDefault();
        ApplyChannelCosts();
        RefreshBroken();
    }

    public void ApplyToEditor(GiftFileEditor editor)
    {
        editor.LikesEvery = int.TryParse(LikesEveryText.Trim(), out var n) ? Math.Max(0, n) : 0;
        editor.LikesEffect = LikesEffect ?? "";
        editor.LikesMore.Clear();
        editor.LikesMore.AddRange(LikesSteps.Model);
        editor.FollowEffect = FollowEffect ?? "";
        editor.FollowMore.Clear();
        editor.FollowMore.AddRange(FollowSteps.Model);
        editor.FollowOncePerUser = FollowOncePerUser;
        editor.ShareEffect = ShareEffect ?? "";
        editor.ShareMore.Clear();
        editor.ShareMore.AddRange(ShareSteps.Model);
        editor.ChatEnabled = ChatEnabled;
        editor.ChatPrefix = string.IsNullOrWhiteSpace(ChatPrefix) ? "!" : ChatPrefix.Trim();
        editor.ChatCooldownSec = ParseWaitSec(ChatCooldownText, 30);
        editor.ChatGlobalGapSec = ParseWaitSec(ChatGlobalGapText, 2);
        editor.ChatCommands.Clear();
        foreach (var cmd in ChatCommands)
        {
            editor.ChatCommands.Add(new EditableChatCommand
            {
                Word = cmd.Word,
                Effect = cmd.Effect,
                MoreSteps = cmd.Steps?.Model.ToList() ?? [],
            });
        }

        editor.TwitchFollowEffect = TwitchFollowEffect ?? "";
        editor.TwitchFollowMore.Clear();
        editor.TwitchFollowMore.AddRange(TwitchFollowSteps.Model);
        editor.TwitchChatEnabled = TwitchChatEnabled;
        editor.TwitchChatPrefix = string.IsNullOrWhiteSpace(TwitchChatPrefix) ? "!" : TwitchChatPrefix.Trim();
        editor.TwitchChatCooldownSec = ParseWaitSec(TwitchChatCooldownText, 30);
        editor.TwitchChatGlobalGapSec = ParseWaitSec(TwitchChatGlobalGapText, 2);
        editor.TwitchChatCommands.Clear();
        foreach (var cmd in TwitchChatCommands)
        {
            editor.TwitchChatCommands.Add(new EditableChatCommand
            {
                Word = cmd.Word,
                Effect = cmd.Effect,
                MoreSteps = cmd.Steps?.Model.ToList() ?? [],
            });
        }

        editor.TwitchBits.Clear();
        foreach (var bit in TwitchBits)
        {
            if (!int.TryParse(bit.MinText.Trim(), out var min) || min < 1)
            {
                continue;
            }

            editor.TwitchBits.Add(new EditableBitsRule
            {
                Min = min,
                Effect = bit.Effect ?? "",
                MoreSteps = bit.Steps?.Model.ToList() ?? [],
            });
        }

        editor.TwitchRewards.Clear();
        foreach (var reward in TwitchRewards)
        {
            editor.TwitchRewards.Add(new EditableTwitchReward
            {
                RewardId = reward.RewardId ?? "",
                Title = reward.Title ?? "",
                Effect = reward.Effect ?? "",
                MoreSteps = reward.Steps?.Model.ToList() ?? [],
            });
        }
    }

    [RelayCommand]
    private async Task ClearFollowHistoryAsync()
    {
        if (!await _shell.Dialogs.ConfirmAsync(
                "Olvidar seguidores",
                "Todos los usuarios volverán a poder disparar el efecto de seguir una vez. ¿Continuar?"))
        {
            return;
        }

        try
        {
            _shell.Runtime.ClearFollowHistory();
            _shell.Dialogs.Info("Olvidar seguidores", "Historial de seguidores borrado.");
        }
        catch (Exception ex)
        {
            _shell.Dialogs.Warn("Olvidar seguidores", ex.Message);
        }
    }

    [RelayCommand]
    private void AddCommand() => AddTo(ChatCommands, cmd => SelectedCommand = cmd);

    [RelayCommand]
    private void RemoveCommand() => RemoveFrom(ChatCommands, SelectedChatCommands, SelectedCommand, v => SelectedCommand = v);

    [RelayCommand]
    private void AddTwitchCommand() => AddTo(TwitchChatCommands, cmd => SelectedTwitchCommand = cmd);

    [RelayCommand]
    private void RemoveTwitchCommand() =>
        RemoveFrom(TwitchChatCommands, SelectedTwitchCommands, SelectedTwitchCommand, v => SelectedTwitchCommand = v);

    [RelayCommand]
    private void AddBitsRule()
    {
        var effect = _shell.EffectChoices.FirstOrDefault(e =>
                         string.Equals(e.Id, "impulse", StringComparison.OrdinalIgnoreCase))?.Id
                     ?? _shell.EffectChoices.FirstOrDefault()?.Id
                     ?? "";
        var row = new BitsRowViewModel { MinText = "1", Effect = effect };
        row.AttachSteps(NewSteps());
        TwitchBits.Add(row);
        SelectedBitsRule = row;
    }

    [RelayCommand]
    private void RemoveBitsRule() =>
        _shell.Undo.RemoveWithUndo(TwitchBits, UndoBarViewModel.Targets(SelectedBitsRules, SelectedBitsRule),
            r => $"Regla de {r.MinText} bits eliminada", n => $"{n} reglas de bits eliminadas", r => SelectedBitsRule = r);

    /// <summary>Nueva regla: la primera recompensa del canal que aún no tiene efecto; sin lista, una para escribir el nombre.</summary>
    [RelayCommand]
    private void AddReward()
    {
        var used = TwitchRewards.Select(r => r.RewardId).Where(id => id.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var next = ChannelRewards.FirstOrDefault(c => !used.Contains(c.Id));
        var row = NewRewardRow(next?.Id ?? "", next?.Title ?? "", DefaultEffect());
        row.AttachSteps(NewSteps());
        TwitchRewards.Add(row);
        ApplyChannelCosts();
        SelectedReward = row;
    }

    [RelayCommand]
    private void RemoveReward() =>
        _shell.Undo.RemoveWithUndo(TwitchRewards, UndoBarViewModel.Targets(SelectedRewards, SelectedReward),
            r => $"Recompensa «{r.DisplayTitle}» eliminada", n => $"{n} recompensas eliminadas", r => SelectedReward = r);

    /// <summary>Al abrir la pestaña Twitch: lee las recompensas del canal una vez (luego, con «Actualizar»).</summary>
    public void EnsureChannelRewards()
    {
        if (!_channelRewardsLoaded && !ChannelRewardsBusy && _shell.Studio.TwitchHasAccount)
        {
            LoadChannelRewardsAsync().FireAndLog("Recompensas de Twitch");
        }
    }

    [RelayCommand]
    private Task RefreshChannelRewardsAsync() => LoadChannelRewardsAsync();

    public async Task LoadChannelRewardsAsync()
    {
        if (ChannelRewardsBusy)
        {
            return;
        }

        ChannelRewardsBusy = true;
        ChannelRewardsStatus = "Leyendo las recompensas de tu canal…";
        try
        {
            var result = await _shell.Runtime.ListTwitchRewardsAsync().ConfigureAwait(true);
            _channelRewardsLoaded = result.Availability != TwitchRewardsAvailability.Error;
            ChannelRewardsNeedRelogin = result.Availability == TwitchRewardsAvailability.NeedsRelogin;
            // Rehacer la lista no es una edición: el desplegable se vacía un momento y vuelve a pintar lo elegido.
            using (_shell.Gifts.AutoSaver.Suppress())
            {
                ChannelRewards.Clear();
                foreach (var reward in result.Rewards)
                {
                    ChannelRewards.Add(new RewardChoice(reward.Id, reward.Title, reward.Cost, reward.IsEnabled && !reward.IsPaused));
                }

                ApplyChannelCosts();
                foreach (var row in TwitchRewards)
                {
                    row.RepaintSelection();
                }
            }

            ChannelRewardsStatus = result.Message;
        }
        finally
        {
            ChannelRewardsBusy = false;
        }
    }

    /// <summary>Tras cerrar la sesión de Twitch: la lista era de esa cuenta.</summary>
    public void ForgetChannelRewards()
    {
        _channelRewardsLoaded = false;
        ChannelRewardsNeedRelogin = false;
        using (_shell.Gifts.AutoSaver.Suppress())
        {
            ChannelRewards.Clear();
            ApplyChannelCosts();
        }

        ChannelRewardsStatus = "";
    }

    partial void OnChannelRewardsStatusChanged(string value) => OnPropertyChanged(nameof(ChannelRewardsHasStatus));

    private RewardRowViewModel NewRewardRow(string rewardId, string title, string effect)
    {
        var row = new RewardRowViewModel { RewardId = rewardId, Title = title, Effect = effect };
        // Elegir otra recompensa en el desplegable trae su nombre (lo que se usa si algún día cambia de id).
        row.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RewardRowViewModel.RewardId) &&
                ChannelRewards.FirstOrDefault(c => string.Equals(c.Id, row.RewardId, StringComparison.OrdinalIgnoreCase))
                    is { } picked)
            {
                row.Title = picked.Title;
                row.Cost = picked.Cost;
            }
        };
        return row;
    }

    private void ApplyChannelCosts()
    {
        foreach (var row in TwitchRewards)
        {
            row.Cost = ChannelRewards.FirstOrDefault(c => string.Equals(c.Id, row.RewardId, StringComparison.OrdinalIgnoreCase))
                ?.Cost ?? 0;
        }
    }

    private string DefaultEffect() =>
        _shell.EffectChoices.FirstOrDefault(e => string.Equals(e.Id, "impulse", StringComparison.OrdinalIgnoreCase))?.Id
        ?? _shell.EffectChoices.FirstOrDefault()?.Id
        ?? "";

    private static int ParseWaitSec(string text, int fallback)
    {
        if (!int.TryParse((text ?? "").Trim(), out var n))
        {
            return fallback;
        }

        return Math.Clamp(n, 0, 3600);
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
        row.AttachSteps(NewSteps());
        list.Add(row);
        select(row);
    }

    private void RemoveFrom(
        ObservableCollection<ChatCommandRowViewModel> list,
        IEnumerable<ChatCommandRowViewModel> selection,
        ChatCommandRowViewModel? current,
        Action<ChatCommandRowViewModel?> assign) =>
        _shell.Undo.RemoveWithUndo(list, UndoBarViewModel.Targets(selection, current),
            c => $"Comando «{c.Word}» eliminado", n => $"{n} comandos eliminados", assign);

    [RelayCommand]
    private void Save()
    {
        _shell.Gifts.SaveManual("Eventos");
    }
}
