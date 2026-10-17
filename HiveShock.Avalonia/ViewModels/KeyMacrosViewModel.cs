using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HiveShock.Avalonia.Services;
using HiveShock.Input;
using HiveShock.Platform.Windows;

namespace HiveShock.Avalonia.ViewModels;

/// <summary>Una ventana abierta que se puede elegir como juego.</summary>
public sealed record OpenWindowChoice(WindowInfo Window)
{
    public string Display => string.IsNullOrWhiteSpace(Window.Title)
        ? Window.ProcessName
        : $"{Window.ProcessName} — {Window.Title}";
}

/// <summary>Página Teclas: macros que pulsan teclas en la ventana del juego y a qué ventana van.</summary>
public sealed partial class KeyMacrosViewModel : ViewModelBase
{
    private readonly MainViewModel _shell;
    private readonly AutoSaver _saver;
    private readonly DispatcherTimer _statusTimer;
    private KeyStepRow? _capturing;
    private bool _capturingHotkey;
    private int _statusBusy;

    public KeyMacrosViewModel(MainViewModel shell)
    {
        _shell = shell;
        SaveStatus = new SaveStatusViewModel(shell.Prefs);
        _saver = new AutoSaver(SaveStatus, SaveToDisk, "Teclas");
        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _statusTimer.Tick += (_, _) => RefreshStatus();

        // De esta página solo cuenta como edición la ventana del juego; las macros se registran fila a fila.
        _saver.Track(this, name => name is not (nameof(WindowProcess) or nameof(WindowTitle) or nameof(WindowRegex)));
        _saver.Track(Macros, name => name is nameof(KeyMacroRow.Title));
        Load();
    }

    public SaveStatusViewModel SaveStatus { get; }

    public ObservableCollection<KeyMacroRow> Macros { get; } = [];
    public ObservableCollection<OpenWindowChoice> OpenWindows { get; } = [];

    public IReadOnlyList<Choice<KeyAction>> ActionChoices => KeyStepRow.ActionChoices;
    public IReadOnlyList<Choice<MacroOverlap>> OverlapChoices => KeyMacroRow.OverlapChoices;
    public IReadOnlyList<string> KeyNames => KeyStepRow.KeyNames;

    [ObservableProperty] private KeyMacroRow? _selectedMacro;
    [ObservableProperty] private OpenWindowChoice? _selectedOpenWindow;
    [ObservableProperty] private string _windowProcess = "";
    [ObservableProperty] private string _windowTitle = "";
    [ObservableProperty] private string _windowRegex = "";
    [ObservableProperty] private string _windowStatusText = "";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private string _validationText = "";

    public bool IsSupported => _shell.Runtime.SupportsKeyInput;
    public bool HasMacros => Macros.Count > 0;
    public bool HasSelection => SelectedMacro != null;
    public bool IsBridgeRunning => _shell.IsRunning;

    public bool HasValidationText => !string.IsNullOrEmpty(ValidationText);

    partial void OnValidationTextChanged(string value) => OnPropertyChanged(nameof(HasValidationText));

    partial void OnSelectedMacroChanged(KeyMacroRow? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        Revalidate();
    }

    partial void OnSelectedOpenWindowChanged(OpenWindowChoice? value)
    {
        if (value is null)
        {
            return;
        }

        // El título suele cambiar (puntuación, nivel…): solo se fija el proceso y se deja el título libre.
        WindowProcess = value.Window.ProcessName;
        WindowTitle = "";
        WindowRegex = "";
        RefreshStatus();
    }

    partial void OnWindowProcessChanged(string value) => RefreshStatus();

    partial void OnWindowTitleChanged(string value) => RefreshStatus();

    partial void OnWindowRegexChanged(string value) => RefreshStatus();

    /// <summary>Carga macros y ventana del perfil activo. No cuenta como edición del usuario.</summary>
    public void Load()
    {
        using var _ = _saver.Suppress();
        Macros.Clear();
        foreach (var macro in _shell.Runtime.LoadKeyMacros())
        {
            AddRow(KeyMacroRow.From(macro));
        }

        var window = _shell.Runtime.GameWindow;
        WindowProcess = window?.ProcessName ?? "";
        WindowTitle = window?.TitleContains ?? "";
        WindowRegex = window?.TitleRegex ?? "";
        SelectedMacro = Macros.FirstOrDefault();
        OnPropertyChanged(nameof(HasMacros));
        OnPropertyChanged(nameof(IsBridgeRunning));
        RefreshStatus();
        _saver.Discard();
    }

    public void FlushPending() => _saver.FlushIfDirty();

    /// <summary>La página está a la vista: se vigila la ventana del juego mientras tanto.</summary>
    public void StartMonitoring()
    {
        OnPropertyChanged(nameof(IsBridgeRunning));
        RefreshOpenWindows();
        RefreshStatus();
        _statusTimer.Start();
    }

    public void StopMonitoring()
    {
        _statusTimer.Stop();
        CancelCapture();
    }

    private WindowTarget CurrentTarget() => new()
    {
        ProcessName = NullIfBlank(WindowProcess),
        TitleContains = NullIfBlank(WindowTitle),
        TitleRegex = NullIfBlank(WindowRegex),
    };

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private void SaveToDisk(bool automatic)
    {
        var models = Macros.Select(m => m.ToModel()).ToList();
        if (models.Any(m => m.Id.Length == 0))
        {
            throw new InvalidOperationException("Hay una macro sin id.");
        }

        var duplicate = models.GroupBy(m => m.Id, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null)
        {
            throw new InvalidOperationException($"Dos macros usan el id «{duplicate.Key}».");
        }

        var target = CurrentTarget();
        var window = target.IsEmpty ? null : target;
        _shell.Runtime.SaveMacroLibrary(models, window);
        _shell.SyncEffectChoices();

        var problems = _shell.Runtime.HotkeyProblems;
        if (problems.Count > 0)
        {
            Message = "Atajos con problema: " + string.Join(" · ", problems.Select(p => $"{p.Key}: {p.Value}"));
        }
        else if (Message.StartsWith("Atajos con problema", StringComparison.Ordinal))
        {
            Message = "";
        }

        Revalidate();
    }

    private void AddRow(KeyMacroRow row)
    {
        row.PropertyChanged += (_, _) => Revalidate();
        row.Steps.CollectionChanged += (_, _) => Revalidate();
        _saver.Track(row.Steps, name => name is nameof(KeyStepRow.IsCapturing) or nameof(KeyStepRow.CaptureLabel));
        foreach (var step in row.Steps)
        {
            step.PropertyChanged += (_, _) => Revalidate();
        }

        row.Steps.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null)
            {
                foreach (KeyStepRow s in e.NewItems)
                {
                    s.PropertyChanged += (_, _) => Revalidate();
                }
            }
        };
        Macros.Add(row);
        OnPropertyChanged(nameof(HasMacros));
    }

    private void Revalidate()
    {
        if (SelectedMacro is null)
        {
            ValidationText = "";
            return;
        }

        var errors = KeyMacroValidator.Validate(SelectedMacro.ToModel()).ToList();
        var id = SelectedMacro.Id.Trim();
        if (id.Length > 0 && Macros.Count(m => string.Equals(m.Id.Trim(), id, StringComparison.OrdinalIgnoreCase)) > 1)
        {
            errors.Insert(0, $"Otra macro ya usa el id «{id}».");
        }

        if (id.Length > 0 && _shell.Runtime.HotkeyProblems.TryGetValue(id, out var hotkeyProblem))
        {
            errors.Add(hotkeyProblem);
        }

        ValidationText = string.Join(Environment.NewLine, errors);
    }

    [RelayCommand]
    private void AddMacro()
    {
        var n = 1;
        while (Macros.Any(m => string.Equals(m.Id, $"teclas_{n}", StringComparison.OrdinalIgnoreCase)))
        {
            n++;
        }

        var row = new KeyMacroRow { Id = $"teclas_{n}", Label = "Nueva macro" };
        row.Steps.Add(new KeyStepRow { Key = "Space" });
        AddRow(row);
        SelectedMacro = row;
        _saver.NotifyChanged();
    }

    [RelayCommand]
    private void DuplicateMacro()
    {
        if (SelectedMacro is null)
        {
            return;
        }

        var copy = KeyMacroRow.From(SelectedMacro.ToModel());
        var n = 2;
        while (Macros.Any(m => string.Equals(m.Id, $"{SelectedMacro.Id}_{n}", StringComparison.OrdinalIgnoreCase)))
        {
            n++;
        }

        copy.Id = $"{SelectedMacro.Id}_{n}";
        copy.Label = $"{SelectedMacro.Label} (copia)";
        AddRow(copy);
        SelectedMacro = copy;
        _saver.NotifyChanged();
    }

    [RelayCommand]
    private void RemoveMacro()
    {
        if (SelectedMacro is null)
        {
            return;
        }

        var index = Macros.IndexOf(SelectedMacro);
        Macros.Remove(SelectedMacro);
        SelectedMacro = Macros.Count == 0 ? null : Macros[Math.Min(index, Macros.Count - 1)];
        OnPropertyChanged(nameof(HasMacros));
    }

    [RelayCommand]
    private void AddStep()
    {
        SelectedMacro?.Steps.Add(new KeyStepRow { Key = "Space" });
    }

    [RelayCommand]
    private void RemoveStep(KeyStepRow? step)
    {
        if (step is null)
        {
            return;
        }

        if (ReferenceEquals(step, _capturing))
        {
            CancelCapture();
        }

        SelectedMacro?.Steps.Remove(step);
    }

    [RelayCommand]
    private void MoveStepUp(KeyStepRow? step) => MoveStep(step, -1);

    [RelayCommand]
    private void MoveStepDown(KeyStepRow? step) => MoveStep(step, 1);

    private void MoveStep(KeyStepRow? step, int delta)
    {
        var steps = SelectedMacro?.Steps;
        if (steps is null || step is null)
        {
            return;
        }

        var from = steps.IndexOf(step);
        var to = from + delta;
        if (from >= 0 && to >= 0 && to < steps.Count)
        {
            steps.Move(from, to);
        }
    }

    [RelayCommand]
    private void BeginCapture(KeyStepRow? step)
    {
        if (step is null)
        {
            return;
        }

        var wasThis = ReferenceEquals(step, _capturing);
        CancelCapture();
        if (!wasThis)
        {
            _capturing = step;
            step.IsCapturing = true;
        }
    }

    public bool IsCapturing => _capturing != null || _capturingHotkey;

    public string HotkeyCaptureLabel => _capturingHotkey ? "Pulsa el atajo…" : "Capturar";

    /// <summary>La página recibió una tecla mientras se capturaba. True si se consumió.</summary>
    public bool CompleteCapture(global::Avalonia.Input.Key key, global::Avalonia.Input.KeyModifiers modifiers)
    {
        if (_capturingHotkey)
        {
            return CompleteHotkeyCapture(key, modifiers);
        }

        if (_capturing is null)
        {
            return false;
        }

        var name = Services.KeyNames.From(key);
        if (name is null)
        {
            Message = "Esa tecla no está soportada; elígela de la lista.";
            CancelCapture();
            return true;
        }

        _capturing.Key = name;
        CancelCapture();
        return true;
    }

    private bool CompleteHotkeyCapture(global::Avalonia.Input.Key key, global::Avalonia.Input.KeyModifiers modifiers)
    {
        // Solo un modificador pulsado: se espera a la tecla principal.
        if (key is global::Avalonia.Input.Key.LeftCtrl or global::Avalonia.Input.Key.RightCtrl
            or global::Avalonia.Input.Key.LeftShift or global::Avalonia.Input.Key.RightShift
            or global::Avalonia.Input.Key.LeftAlt or global::Avalonia.Input.Key.RightAlt
            or global::Avalonia.Input.Key.LWin or global::Avalonia.Input.Key.RWin)
        {
            return true;
        }

        var wasCancel = key == global::Avalonia.Input.Key.Escape && modifiers == global::Avalonia.Input.KeyModifiers.None;
        CancelCapture();
        if (wasCancel || SelectedMacro is null)
        {
            return true;
        }

        var name = Services.KeyNames.From(key);
        uint mods = 0;
        if (modifiers.HasFlag(global::Avalonia.Input.KeyModifiers.Control)) mods |= HotkeyCombo.Ctrl;
        if (modifiers.HasFlag(global::Avalonia.Input.KeyModifiers.Alt)) mods |= HotkeyCombo.Alt;
        if (modifiers.HasFlag(global::Avalonia.Input.KeyModifiers.Shift)) mods |= HotkeyCombo.Shift;
        if (modifiers.HasFlag(global::Avalonia.Input.KeyModifiers.Meta)) mods |= HotkeyCombo.Win;

        if (name is null || mods == 0 || !HotkeyCombo.TryParse(HotkeyCombo.Format(mods, name), out var combo))
        {
            Message = "El atajo necesita Ctrl, Alt, Shift o Win más una tecla (p. ej. Ctrl+Alt+F5).";
            return true;
        }

        SelectedMacro.HotkeyText = combo.Text;
        return true;
    }

    [RelayCommand]
    private void BeginHotkeyCapture()
    {
        var was = _capturingHotkey;
        CancelCapture();
        _capturingHotkey = !was;
        OnPropertyChanged(nameof(HotkeyCaptureLabel));
    }

    [RelayCommand]
    private void ClearHotkey()
    {
        if (SelectedMacro != null)
        {
            SelectedMacro.HotkeyText = "";
        }
    }

    private void CancelCapture()
    {
        if (_capturingHotkey)
        {
            _capturingHotkey = false;
            OnPropertyChanged(nameof(HotkeyCaptureLabel));
        }

        if (_capturing != null)
        {
            _capturing.IsCapturing = false;
            _capturing = null;
        }
    }

    [RelayCommand]
    private void TestMacro()
    {
        if (SelectedMacro is null)
        {
            return;
        }

        _saver.FlushIfDirty();
        if (CurrentTarget().IsEmpty)
        {
            Message = "Primero indica la ventana del juego (arriba).";
            return;
        }

        var errors = _shell.Runtime.TestKeyMacro(SelectedMacro.ToModel());
        Message = errors.Count == 0
            ? "Enviando teclas a la ventana del juego…"
            : "No se puede probar: " + errors[0];
    }

    [RelayCommand]
    private void RefreshOpenWindows()
    {
        OpenWindows.Clear();
        foreach (var w in _shell.Runtime.ListGameWindows()
                     .Where(w => w.ProcessName.Length > 0 && w.ProcessId != Environment.ProcessId)
                     .OrderBy(w => w.ProcessName, StringComparer.OrdinalIgnoreCase))
        {
            OpenWindows.Add(new OpenWindowChoice(w));
        }
    }

    private void RefreshStatus()
    {
        var target = CurrentTarget();
        if (!IsSupported)
        {
            WindowStatusText = "Este sistema no puede enviar teclas a otra ventana (solo Windows).";
            return;
        }

        if (target.IsEmpty)
        {
            WindowStatusText = "Sin ventana: elige el juego en la lista o escribe el nombre de su proceso.";
            return;
        }

        if (Interlocked.Exchange(ref _statusBusy, 1) == 1)
        {
            return;
        }

        // Enumerar ventanas y procesos puede tardar: fuera del hilo de la interfaz.
        _ = Task.Run(() =>
        {
            try
            {
                var status = _shell.Runtime.GetGameWindowStatus(target);
                var text = status switch
                {
                    WindowStatus.Ready => "🟢 Lista: el juego está abierto.",
                    WindowStatus.Minimized => "🟡 Minimizada: se restaurará al enviar teclas.",
                    WindowStatus.Elevated => "⚠ El juego corre como administrador. Ejecuta HiveShock como administrador para poder enviarle teclas.",
                    WindowStatus.NotFound => "🔴 No se encuentra la ventana. ¿Está abierto el juego?",
                    _ => "Este sistema no puede enviar teclas.",
                };
                Dispatcher.UIThread.Post(() => WindowStatusText = text);
            }
            finally
            {
                Interlocked.Exchange(ref _statusBusy, 0);
            }
        });
    }
}
