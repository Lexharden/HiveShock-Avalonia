using CommunityToolkit.Mvvm.ComponentModel;

namespace HiveShock.Avalonia.ViewModels;

public sealed partial class GoalRowViewModel : ViewModelBase
{
    private string _effect = "";

    [ObservableProperty] private string _goalId = "";
    [ObservableProperty] private string _label = "";
    [ObservableProperty] private string _needText = "2";

    public string Effect
    {
        get => _effect;
        set => SetEffectId(ref _effect, value);
    }

    [ObservableProperty] private string _giftsText = "";
    [ObservableProperty] private bool _alsoInstant = true;
    [ObservableProperty] private bool _repeat = true;
    [ObservableProperty] private string _progressText = "0/2";
    [ObservableProperty] private bool _closed;

    /// <summary>Efectos que se lanzan después del principal.</summary>
    public StepListViewModel Steps { get; private set; } = null!;

    /// <summary>«+2 efectos» en la lista; también avisa al guardado automático cuando se edita un efecto adicional.</summary>
    public string StepSummary => Steps?.Summary ?? "";

    public bool HasExtraSteps => Steps is { HasSteps: true };

    public void AttachSteps(StepListViewModel steps)
    {
        Steps = steps;
        steps.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(StepSummary));
            OnPropertyChanged(nameof(HasExtraSteps));
        };
        OnPropertyChanged(nameof(Steps));
    }
}
