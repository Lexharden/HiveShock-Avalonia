using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HiveShock.Configuration;
using HiveShock.Live;

namespace HiveShock.Avalonia.ViewModels;

public sealed partial class GoalsViewModel : ViewModelBase
{
    private readonly MainViewModel _shell;

    public GoalsViewModel(MainViewModel shell)
    {
        _shell = shell;
        _shell.Runtime.Goals.Changed += (_, _) =>
            Dispatcher.UIThread.Post(RefreshProgress);
    }

    [ObservableProperty] private GoalRowViewModel? _selected;

    public IEnumerable<EffectChoice> EffectsWithNone =>
        new EffectChoice[] { new("", "Ninguno") }.Concat(_shell.EffectChoices);

    public ObservableCollection<GoalRowViewModel> Rows { get; } = [];

    public void NotifyEffects() => OnPropertyChanged(nameof(EffectsWithNone));

    public void LoadFromEditor(GiftFileEditor editor)
    {
        Rows.Clear();
        foreach (var goal in editor.Goals)
        {
            Rows.Add(new GoalRowViewModel
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

        Selected = Rows.FirstOrDefault();
        RefreshProgress();
    }

    public void ApplyToEditor(GiftFileEditor editor)
    {
        editor.Goals.Clear();
        foreach (var row in Rows)
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

    public void RefreshProgress()
    {
        var snaps = _shell.Runtime.Goals.Snapshots();
        foreach (var row in Rows)
        {
            var snap = Match(snaps, row);
            if (snap == null)
            {
                row.ProgressText = $"0/{ParseNeed(row)}";
                row.Closed = false;
                continue;
            }

            row.ProgressText = snap.ProgressText;
            row.Closed = snap.Closed;
            if (string.IsNullOrWhiteSpace(row.GoalId))
            {
                row.GoalId = snap.Id;
            }
        }
    }

    [RelayCommand]
    private void Add()
    {
        var effect = _shell.EffectChoices.FirstOrDefault(e =>
                         string.Equals(e.Id, "delete_save", StringComparison.OrdinalIgnoreCase))?.Id
                     ?? _shell.EffectChoices.FirstOrDefault()?.Id
                     ?? "";
        var row = new GoalRowViewModel
        {
            Label = "Reinicio",
            NeedText = "2",
            Effect = effect,
            GiftsText = "Galaxy",
            AlsoInstant = true,
            Repeat = true,
            ProgressText = "0/2",
        };
        Rows.Add(row);
        Selected = row;
    }

    [RelayCommand]
    private void Remove()
    {
        if (Selected == null)
        {
            return;
        }

        Rows.Remove(Selected);
        Selected = Rows.FirstOrDefault();
    }

    [RelayCommand]
    private void ResetSelected()
    {
        if (Selected == null)
        {
            return;
        }

        var id = Selected.GoalId;
        if (string.IsNullOrWhiteSpace(id))
        {
            var snap = Match(_shell.Runtime.Goals.Snapshots(), Selected);
            id = snap?.Id ?? "";
        }

        if (!string.IsNullOrWhiteSpace(id))
        {
            _shell.Runtime.Goals.Reset(id);
        }

        Selected.ProgressText = $"0/{ParseNeed(Selected)}";
        Selected.Closed = false;
    }

    [RelayCommand]
    private async Task Simulate()
    {
        if (Selected == null)
        {
            return;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(Selected.GoalId) ||
                !_shell.Runtime.Goals.Snapshots().Any(s =>
                    string.Equals(s.Id, Selected.GoalId, StringComparison.OrdinalIgnoreCase)))
            {
                _shell.Gifts.SaveToDisk();
                RefreshProgress();
            }

            var id = Selected.GoalId;
            if (string.IsNullOrWhiteSpace(id))
            {
                var snap = _shell.Runtime.Goals.Snapshots().FirstOrDefault(s =>
                    string.Equals(s.Label, Selected.Label, StringComparison.OrdinalIgnoreCase));
                id = snap?.Id ?? "";
            }

            if (string.IsNullOrWhiteSpace(id))
            {
                _shell.Dialogs.Warn("Metas", "Guarda la meta antes de simular.");
                return;
            }

            await _shell.Runtime.SimulateGoalAsync(id).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _shell.Dialogs.Error("Metas", ex.Message);
        }
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
            _shell.Dialogs.Error("Metas", ex.Message);
        }
    }

    private static GoalSnapshot? Match(IReadOnlyList<GoalSnapshot> snaps, GoalRowViewModel row)
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

    private static int ParseNeed(GoalRowViewModel row) =>
        int.TryParse(row.NeedText.Trim(), out var n) && n > 0 ? n : 2;
}
