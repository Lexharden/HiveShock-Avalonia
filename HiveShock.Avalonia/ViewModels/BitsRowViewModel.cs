using CommunityToolkit.Mvvm.ComponentModel;

namespace HiveShock.Avalonia.ViewModels;

public sealed partial class BitsRowViewModel : ViewModelBase
{
    private string _effect = "";

    [ObservableProperty] private string _minText = "1";

    public string Effect
    {
        get => _effect;
        set => SetEffectId(ref _effect, value);
    }

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
