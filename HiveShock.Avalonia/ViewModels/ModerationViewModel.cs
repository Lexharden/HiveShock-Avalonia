using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HiveShock.Configuration;
using HiveShock.Live;

namespace HiveShock.Avalonia.ViewModels;

/// <summary>Quién activó efectos hace poco (agrupado por persona) y si se puede bloquear.</summary>
public sealed class RecentViewerRow
{
    public required ViewerIdentity Viewer { get; init; }
    public required string Title { get; init; }
    public required string Detail { get; init; }
    public required bool CanBlock { get; init; }
    public string BlockedText => CanBlock ? "" : Viewer.IsIdentified ? "Ya bloqueado" : "Sin identificar";
}

public sealed class BlockedViewerRow
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Detail { get; init; }
}

/// <summary>Página Moderación: pausa de emergencia y lista de bloqueo de espectadores.</summary>
public sealed partial class ModerationViewModel : ViewModelBase
{
    private readonly MainViewModel _shell;
    private readonly ViewerGuard _guard;
    private int _refreshQueued;

    public ModerationViewModel(MainViewModel shell)
    {
        _shell = shell;
        _guard = shell.Runtime.Guard;
        _guard.Changed += QueueRefresh;
        Refresh();
    }

    public ObservableCollection<RecentViewerRow> Recent { get; } = [];
    public ObservableCollection<BlockedViewerRow> Blocked { get; } = [];

    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private string _newHandle = "";
    [ObservableProperty] private bool _newIsTwitch;
    [ObservableProperty] private string _message = "";

    public bool NewIsTikTok
    {
        get => !NewIsTwitch;
        set => NewIsTwitch = !value;
    }

    public bool HasRecent => Recent.Count > 0;
    public bool HasBlocked => Blocked.Count > 0;
    public string PauseLabel => IsPaused ? "Reanudar efectos" : "Pausar efectos";

    public string PauseStatus => IsPaused
        ? "Efectos pausados: nada de lo que hagan los espectadores llega al juego."
        : "Efectos activos.";

    partial void OnIsPausedChanged(bool value)
    {
        OnPropertyChanged(nameof(PauseLabel));
        OnPropertyChanged(nameof(PauseStatus));
    }

    partial void OnNewIsTwitchChanged(bool value) => OnPropertyChanged(nameof(NewIsTikTok));

    [RelayCommand]
    private void TogglePause() => _guard.SetPaused(!_guard.IsPaused);

    [RelayCommand]
    private void Block(RecentViewerRow? row)
    {
        if (row == null)
        {
            return;
        }

        Message = _guard.Block(row.Viewer)
            ? $"Bloqueado: {row.Viewer.Label}. Sus eventos ya no activan efectos."
            : "No se pudo bloquear: ya estaba bloqueado o no hay forma de reconocerlo.";
        Refresh();
    }

    [RelayCommand]
    private void Unblock(BlockedViewerRow? row)
    {
        if (row == null)
        {
            return;
        }

        if (_guard.Unblock(row.Id))
        {
            Message = $"Desbloqueado: {row.Title}.";
        }

        Refresh();
    }

    [RelayCommand]
    private void AddHandle()
    {
        var port = NewIsTwitch ? LivePortIds.Twitch : LivePortIds.TikTok;
        var raw = NewHandle ?? "";
        // En TikTok también se acepta pegar el enlace del perfil.
        var handle = ViewerIdentity.NormalizeHandle(NewIsTwitch ? raw : BridgeOptions.NormalizeUniqueId(raw));
        if (handle.Length == 0)
        {
            Message = "Escribe el @usuario de la persona.";
            return;
        }

        if (_guard.BlockHandle(port, handle))
        {
            Message = $"Bloqueado: @{handle}. Cuando HiveShock lo vea activo, también aprende su id.";
            NewHandle = "";
        }
        else
        {
            Message = $"@{handle} ya estaba bloqueado.";
        }

        Refresh();
    }

    /// <summary>Los cambios llegan seguidos (cada evento anota actividad): se agrupan en una sola actualización.</summary>
    private void QueueRefresh()
    {
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 0)
        {
            Dispatcher.UIThread.Post(() =>
            {
                Interlocked.Exchange(ref _refreshQueued, 0);
                Refresh();
            }, DispatcherPriority.Background);
        }
    }

    private void Refresh()
    {
        IsPaused = _guard.IsPaused;

        var recent = _guard.Recent
            .GroupBy(a => $"{a.Viewer.PortId}:{a.Viewer.StableKey}")
            .Select(g =>
            {
                var last = g.First();
                var count = g.Count();
                var what = count > 1 ? $"{last.What} (+{count - 1} más)" : last.What;
                return new RecentViewerRow
                {
                    Viewer = last.Viewer,
                    Title = $"{last.Viewer.Label} · {PortName(last.Viewer.PortId)}",
                    Detail = $"{last.AtUtc.ToLocalTime():HH:mm:ss}  {what}" +
                             (last.Verdict == ViewerVerdict.Paused ? " — no se aplicó (pausa)" : ""),
                    CanBlock = last.Viewer.IsIdentified && !_guard.IsBlocked(last.Viewer),
                };
            })
            .ToList();
        Replace(Recent, recent);

        var blocked = _guard.Blocked
            .Select(b => new BlockedViewerRow
            {
                Id = b.Id,
                Title = $"{b.Identity.Label} · {PortName(b.PortId)}",
                Detail = $"{b.Identity.Detail} · desde {b.AddedUtc.ToLocalTime():dd/MM HH:mm}",
            })
            .ToList();
        Replace(Blocked, blocked);

        OnPropertyChanged(nameof(HasRecent));
        OnPropertyChanged(nameof(HasBlocked));
    }

    private static void Replace<T>(ObservableCollection<T> target, List<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    private static string PortName(string portId) =>
        string.Equals(portId, LivePortIds.Twitch, StringComparison.OrdinalIgnoreCase) ? "Twitch" : "TikTok";
}
