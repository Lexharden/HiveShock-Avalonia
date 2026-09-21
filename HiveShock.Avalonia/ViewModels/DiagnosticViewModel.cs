using System.Collections.ObjectModel;
using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HiveShock.Live;

namespace HiveShock.Avalonia.ViewModels;

public sealed partial class DiagnosticViewModel : ViewModelBase
{
#if DEBUG
    private readonly LiveDiagnosticSink _sink;
    private const int MaxDisplay = 500;

    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private string _filterCategory = "Todos";
    [ObservableProperty] private string _statsText = "";
    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private int _droppedCount;

    public ObservableCollection<DiagnosticEvent> Events { get; } = [];

    public IReadOnlyList<string> CategoryOptions { get; } =
        ["Todos", "Gift", "Chat", "Like", "Follow", "Share", "Join", "Barrage", "System", "Unknown"];

    public DiagnosticViewModel(LiveDiagnosticSink sink)
    {
        _sink = sink;
        _sink.Changed += OnSinkChanged;
    }

    private void OnSinkChanged()
    {
        if (IsPaused) return;
        Dispatcher.UIThread.Post(Refresh);
    }

    private void Refresh()
    {
        var (all, dropped) = _sink.Snapshot();
        DroppedCount = dropped;

        var filter = FilterText.Trim();
        var cat    = FilterCategory;

        var filtered = all
            .Where(e => cat == "Todos" || e.Category == cat)
            .Where(e => string.IsNullOrEmpty(filter) ||
                        e.Summary.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                        e.RawType.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .Take(MaxDisplay)
            .ToList();

        Events.Clear();
        foreach (var e in filtered)
            Events.Add(e);

        // Stats
        var sb = new StringBuilder();
        var byCategory = all.GroupBy(e => e.Category).OrderByDescending(g => g.Count());
        foreach (var g in byCategory)
            sb.Append($"{g.Key}: {g.Count()}  ");
        if (dropped > 0) sb.Append($"(+{dropped} descartados)");
        StatsText = sb.ToString().Trim();
    }

    partial void OnFilterTextChanged(string value) => Refresh();
    partial void OnFilterCategoryChanged(string value) => Refresh();

    [RelayCommand]
    private void Clear()
    {
        _sink.Clear();
        Events.Clear();
        StatsText = "";
        DroppedCount = 0;
    }

    [RelayCommand]
    private void TogglePause()
    {
        IsPaused = !IsPaused;
        if (!IsPaused)
        {
            Refresh();
        }
    }

    [RelayCommand]
    private void Export()
    {
        try
        {
            var (all, _) = _sink.Snapshot();
            var sb = new StringBuilder();
            sb.AppendLine("Timestamp\tSource\tType\tCategory\tSummary\tDetail");
            foreach (var e in all)
                sb.AppendLine($"{e.Timestamp:HH:mm:ss.fff}\t{e.Source}\t{e.RawType}\t{e.Category}\t{e.Summary}\t{e.Detail ?? ""}");
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var path = Path.Combine(
                string.IsNullOrWhiteSpace(desktop) ? AppContext.BaseDirectory : desktop,
                $"HiveShock-diagnostico-{DateTime.Now:yyyyMMdd-HHmmss}.tsv");
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            StatsText = $"Exportado con éxito: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            StatsText = $"Error exportando: {ex.Message}";
        }
    }
#else
    public ObservableCollection<DiagnosticEvent> Events { get; } = [];
    public IReadOnlyList<string> CategoryOptions { get; } = ["Todos"];
    public string FilterText { get; set; } = "";
    public string FilterCategory { get; set; } = "Todos";
    public string StatsText { get; set; } = "";
    public bool IsPaused => false;
    public int DroppedCount => 0;

    public DiagnosticViewModel(LiveDiagnosticSink sink) { }

    [RelayCommand]
    private void Clear() { }

    [RelayCommand]
    private void TogglePause() { }

    [RelayCommand]
    private void Export() { }
#endif
}
