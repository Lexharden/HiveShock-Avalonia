using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using HiveShock.Avalonia.Services;

namespace HiveShock.Avalonia.ViewModels;

public enum SaveState
{
    Idle,
    /// <summary>Hay cambios en pantalla que todavía no están en disco.</summary>
    Pending,
    Saving,
    Saved,
    Failed,
}

/// <summary>
/// Estado de guardado de un grupo de datos (por ejemplo gifts.json): lo que muestra la etiqueta
/// «Guardado» / «Sin guardar» / «No se pudo guardar» y la casilla de guardado automático.
/// </summary>
public sealed partial class SaveStatusViewModel : ViewModelBase
{
    private static readonly TimeSpan SavedVisibleFor = TimeSpan.FromSeconds(4);

    private readonly UiPreferences? _prefs;
    private readonly DispatcherTimer _hideTimer;

    /// <param name="prefs">Preferencias donde vive «guardado automático». Null = sin casilla (solo manual).</param>
    public SaveStatusViewModel(UiPreferences? prefs = null)
    {
        _prefs = prefs;
        _hideTimer = new DispatcherTimer { Interval = SavedVisibleFor };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            if (State == SaveState.Saved)
            {
                Set(SaveState.Idle, "");
            }
        };
    }

    [ObservableProperty] private SaveState _state = SaveState.Idle;
    [ObservableProperty] private string _message = "";

    /// <summary>Se dispara al activar el guardado automático (para volcar lo que estaba pendiente).</summary>
    public event Action? AutoSaveTurnedOn;

    public bool SupportsAutoSave => _prefs != null;

    public bool AutoSaveEnabled
    {
        get => _prefs?.AutoSaveEnabled ?? false;
        set
        {
            if (_prefs == null || _prefs.AutoSaveEnabled == value)
            {
                return;
            }

            _prefs.AutoSaveEnabled = value;
            _prefs.Save();
            OnPropertyChanged();
            if (value)
            {
                AutoSaveTurnedOn?.Invoke();
            }
            else if (State == SaveState.Pending)
            {
                Set(SaveState.Pending, PendingText(false));
            }
        }
    }

    public bool IsVisible => State != SaveState.Idle;
    public bool IsPending => State is SaveState.Pending or SaveState.Saving;
    public bool IsSaved => State == SaveState.Saved;
    public bool IsFailed => State == SaveState.Failed;

    partial void OnStateChanged(SaveState value)
    {
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(IsPending));
        OnPropertyChanged(nameof(IsSaved));
        OnPropertyChanged(nameof(IsFailed));
    }

    public void MarkPending() => Set(SaveState.Pending, PendingText(AutoSaveEnabled));

    public void MarkSaving() => Set(SaveState.Saving, "Guardando…");

    public void MarkSaved(string what) =>
        Set(SaveState.Saved, $"✓ {what} guardado · {DateTime.Now:HH:mm:ss}");

    public void MarkFailed(string reason) =>
        Set(SaveState.Failed, $"✗ No se pudo guardar: {reason}");

    private static string PendingText(bool auto) =>
        auto ? "Cambios detectados · guardando en un momento…" : "Cambios sin guardar";

    private void Set(SaveState state, string message)
    {
        _hideTimer.Stop();
        Message = message;
        State = state;
        if (state == SaveState.Saved)
        {
            _hideTimer.Start();
        }
    }
}
