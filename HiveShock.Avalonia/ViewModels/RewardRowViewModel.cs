using CommunityToolkit.Mvvm.ComponentModel;

namespace HiveShock.Avalonia.ViewModels;

/// <summary>Una recompensa de puntos del canal de Twitch y qué hace en el juego.</summary>
public sealed partial class RewardRowViewModel : ViewModelBase
{
    private string _effect = "";
    private string _rewardId = "";

    /// <summary>
    /// Id de la recompensa en Twitch (vacío si se escribió solo el nombre a mano). El desplegable manda null al
    /// quedarse sin opciones (recargar la lista del canal): eso no es una elección del usuario y se ignora.
    /// </summary>
    public string RewardId
    {
        get => _rewardId;
        set
        {
            if (value is null)
            {
                OnPropertyChanged();
                return;
            }

            if (SetProperty(ref _rewardId, value))
            {
                OnPropertyChanged(nameof(IsManual));
            }
        }
    }

    /// <summary>Nombre de la recompensa: lo que se ve en la lista y lo que se usa si el id no coincide.</summary>
    [ObservableProperty] private string _title = "";

    /// <summary>Coste en puntos según la última lista del canal (0 = desconocido). Solo informativo.</summary>
    [ObservableProperty] private int _cost;

    public string Effect
    {
        get => _effect;
        set => SetEffectId(ref _effect, value);
    }

    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "Recompensa sin nombre" : Title.Trim();

    /// <summary>« · 500 puntos» detrás del nombre en la lista; vacío si no se conoce el coste.</summary>
    public string CostText => Cost > 0 ? $" · {Cost:N0} puntos" : "";

    /// <summary>Sin id solo se puede reconocer por el nombre: se muestra el campo para escribirlo.</summary>
    public bool IsManual => string.IsNullOrWhiteSpace(RewardId);

    /// <summary>Efectos que se lanzan después del principal.</summary>
    public StepListViewModel Steps { get; private set; } = null!;

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

    partial void OnTitleChanged(string value) => OnPropertyChanged(nameof(DisplayTitle));

    /// <summary>Tras rehacer la lista del desplegable, que vuelva a mostrar la recompensa elegida.</summary>
    public void RepaintSelection() => OnPropertyChanged(nameof(RewardId));

    partial void OnCostChanged(int value) => OnPropertyChanged(nameof(CostText));
}

/// <summary>Una recompensa del canal en el desplegable.</summary>
public sealed record RewardChoice(string Id, string Title, int Cost, bool IsActive)
{
    public string Display =>
        $"{Title} · {Cost:N0} puntos{(IsActive ? "" : " (pausada o desactivada en Twitch)")}";
}
