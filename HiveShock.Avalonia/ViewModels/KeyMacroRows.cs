using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using HiveShock.Input;

namespace HiveShock.Avalonia.ViewModels;

public sealed record Choice<T>(T Value, string Display);

/// <summary>Un paso editable de una macro. Los números se editan como texto (igual que el resto de páginas).</summary>
public sealed partial class KeyStepRow : ObservableObject
{
    public static IReadOnlyList<string> KeyNames { get; } = [.. GameKeys.Names];

    public static IReadOnlyList<Choice<KeyAction>> ActionChoices { get; } =
    [
        new(KeyAction.Tap, "Pulsar"),
        new(KeyAction.Hold, "Mantener"),
        new(KeyAction.Down, "Dejar pulsada"),
        new(KeyAction.Up, "Soltar"),
    ];

    [ObservableProperty] private string _key = "Space";
    [ObservableProperty] private KeyAction _action = KeyAction.Tap;
    [ObservableProperty] private string _holdText = "500";
    [ObservableProperty] private string _delayText = "0";
    [ObservableProperty] private string _repeatText = "1";
    [ObservableProperty] private string _jitterText = "0";
    [ObservableProperty] private bool _isCapturing;

    public bool ShowHold => Action == KeyAction.Hold;

    public bool ShowRepeat => Action is KeyAction.Tap or KeyAction.Hold;

    public string CaptureLabel => IsCapturing ? "Pulsa una tecla…" : "Capturar";

    partial void OnActionChanged(KeyAction value)
    {
        OnPropertyChanged(nameof(ShowHold));
        OnPropertyChanged(nameof(ShowRepeat));
    }

    partial void OnIsCapturingChanged(bool value) => OnPropertyChanged(nameof(CaptureLabel));

    public static KeyStepRow From(KeyStep s) => new()
    {
        Key = s.Key,
        Action = s.Action,
        HoldText = s.HoldMs.ToString(CultureInfo.InvariantCulture),
        DelayText = s.DelayAfterMs.ToString(CultureInfo.InvariantCulture),
        RepeatText = s.Repeat.ToString(CultureInfo.InvariantCulture),
        JitterText = s.JitterPercent.ToString(CultureInfo.InvariantCulture),
    };

    public KeyStep ToModel() => new()
    {
        Key = Key,
        Action = Action,
        HoldMs = ParseInt(HoldText, 500),
        DelayAfterMs = ParseInt(DelayText, 0),
        Repeat = ParseInt(RepeatText, 1),
        JitterPercent = ParseInt(JitterText, 0),
    };

    private static int ParseInt(string? text, int fallback) =>
        int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;
}

public sealed partial class KeyMacroRow : ObservableObject
{
    public static IReadOnlyList<Choice<MacroOverlap>> OverlapChoices { get; } =
    [
        new(MacroOverlap.Queue, "Ponerla en cola"),
        new(MacroOverlap.Drop, "Ignorar si ya está en marcha"),
        new(MacroOverlap.Replace, "Reiniciarla"),
    ];

    [ObservableProperty] private string _id = "";
    [ObservableProperty] private string _label = "";
    [ObservableProperty] private MacroOverlap _overlap = MacroOverlap.Queue;
    [ObservableProperty] private bool _allowDangerous;
    [ObservableProperty] private string _maxTotalText = "10000";
    [ObservableProperty] private string _hotkeyText = "";

    public ObservableCollection<KeyStepRow> Steps { get; } = [];

    public string Title => string.IsNullOrWhiteSpace(Label) ? Id : Label;

    partial void OnLabelChanged(string value) => OnPropertyChanged(nameof(Title));

    partial void OnIdChanged(string value) => OnPropertyChanged(nameof(Title));

    public static KeyMacroRow From(KeyMacro m)
    {
        var row = new KeyMacroRow
        {
            Id = m.Id,
            Label = m.Label,
            Overlap = m.Overlap,
            AllowDangerous = m.AllowDangerous,
            MaxTotalText = m.MaxTotalMs.ToString(CultureInfo.InvariantCulture),
            HotkeyText = m.Hotkey ?? "",
        };
        foreach (var s in m.Steps)
        {
            row.Steps.Add(KeyStepRow.From(s));
        }

        return row;
    }

    public KeyMacro ToModel() => new()
    {
        Id = Id.Trim(),
        Label = Label.Trim(),
        Overlap = Overlap,
        AllowDangerous = AllowDangerous,
        MaxTotalMs = int.TryParse(MaxTotalText?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var max) && max > 0
            ? max
            : 10_000,
        Hotkey = string.IsNullOrWhiteSpace(HotkeyText) ? null : HotkeyText.Trim(),
        Steps = [.. Steps.Select(s => s.ToModel())],
    };
}
