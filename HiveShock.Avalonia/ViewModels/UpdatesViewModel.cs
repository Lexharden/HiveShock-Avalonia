using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HiveShock.Configuration;
using HiveShock.Logging;
using HiveShock.Updates;

namespace HiveShock.Avalonia.ViewModels;

/// <summary>Una versión publicada en GitHub Releases.</summary>
public sealed class ReleaseRow
{
    public required ReleaseInfo Release { get; init; }
    public required string Version { get; init; }
    public required string Title { get; init; }
    public required string DateText { get; init; }
    public required string Badge { get; init; }
    public required string BadgeTone { get; init; }
    public required bool IsPrerelease { get; init; }
    public required string Notes { get; init; }
    public required bool CanInstall { get; init; }
    public required string ActionText { get; init; }
    public required string Unavailable { get; init; }
    public bool HasNotes => Notes.Length > 0;
    public bool IsUnavailable => Unavailable.Length > 0;
    public bool IsNewer => BadgeTone == "newer";
}

/// <summary>Página Actualizaciones: busca, descarga e instala versiones desde los Releases de GitHub.</summary>
public sealed partial class UpdatesViewModel : ViewModelBase
{
    private readonly MainViewModel _shell;
    private readonly UpdateService _service;
    private string _announcedVersion = "";
    private bool _syncing;

    public UpdatesViewModel(MainViewModel shell)
    {
        _shell = shell;
        var prefs = shell.Prefs;
        var options = new UpdateOptions
        {
            CheckOnStart = prefs.UpdateCheckEnabled,
            AutoDownload = prefs.UpdateAutoDownload,
            IncludePrereleases = prefs.UpdateIncludePrereleases,
            SkippedVersion = prefs.UpdateSkippedVersion,
        };

        // Timeout infinito: las descargas son largas; las consultas llevan su propio límite.
        var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _service = new UpdateService(http, shell.About.Version, options);
        _service.Changed += () => Dispatcher.UIThread.Post(Refresh);

        _syncing = true;
        CheckOnStart = options.CheckOnStart;
        AutoDownload = options.AutoDownload;
        IncludePrereleases = options.IncludePrereleases;
        _syncing = false;
        Refresh();
    }

    public ObservableCollection<ReleaseRow> Rows { get; } = [];

    public string CurrentVersionText => $"Versión instalada: {_service.CurrentVersion}";

    [ObservableProperty] private bool _checkOnStart = true;
    [ObservableProperty] private bool _autoDownload = true;
    [ObservableProperty] private bool _includePrereleases;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _statusDetail = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _showProgress;
    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private bool _hasUpdate;
    [ObservableProperty] private string _badgeText = "";
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private string _installLatestText = "";

    public bool HasRows => Rows.Count > 0;

    /// <summary>Chip de la barra superior: hay una versión nueva sin omitir.</summary>
    public bool BadgeVisible => HasUpdate;

    partial void OnCheckOnStartChanged(bool value) => OptionChanged();
    partial void OnAutoDownloadChanged(bool value) => OptionChanged();

    partial void OnIncludePrereleasesChanged(bool value)
    {
        OptionChanged();
        Refresh();
    }

    private void OptionChanged()
    {
        if (_syncing)
        {
            return;
        }

        var o = _service.Options;
        o.CheckOnStart = CheckOnStart;
        o.AutoDownload = AutoDownload;
        o.IncludePrereleases = IncludePrereleases;
        SavePrefs();
        Refresh();
    }

    private void SavePrefs()
    {
        var p = _shell.Prefs;
        p.UpdateCheckEnabled = _service.Options.CheckOnStart;
        p.UpdateAutoDownload = _service.Options.AutoDownload;
        p.UpdateIncludePrereleases = _service.Options.IncludePrereleases;
        p.UpdateSkippedVersion = _service.Options.SkippedVersion;
        p.Save();
    }

    /// <summary>Llamar al abrir la ventana: confirma un arranque tras actualizar, limpia y empieza a comprobar.</summary>
    public void Start()
    {
        var paths = _service.Paths;
        UpdateStartup.Confirm(paths, _service.CurrentVersion);
        var result = UpdateStartup.ConsumeResult(paths);
        UpdateStartup.Cleanup(paths, _service.Downloaded?.Version);

        if (result is not null)
        {
            if (result.Ok)
            {
                BridgeLog.Info($"HiveShock se actualizó a la versión {result.ToVersion}.");
                _shell.Dialogs.Info("Actualización lista", $"HiveShock se actualizó a la versión {result.ToVersion}.");
            }
            else
            {
                var restored = result.RolledBack ? "Se restauró la versión anterior." : "No se pudo restaurar del todo la versión anterior.";
                BridgeLog.Warn($"La actualización a {result.ToVersion} falló: {result.Error}");
                _shell.Dialogs.Warn("La actualización falló",
                    $"No se pudo instalar la versión {result.ToVersion}. {restored}\n\nMotivo: {result.Error}");
            }
        }

        _service.Start();
    }

    [RelayCommand]
    private async Task CheckNowAsync()
    {
        await _service.CheckAsync(silent: false).ConfigureAwait(true);
    }

    [RelayCommand]
    private Task InstallLatestAsync() =>
        _service.Latest is { } latest ? InstallAsync(latest) : Task.CompletedTask;

    [RelayCommand]
    private Task InstallRowAsync(ReleaseRow? row) => row is null ? Task.CompletedTask : InstallAsync(row.Release);

    [RelayCommand]
    private void SkipLatest()
    {
        if (_service.Announceable is { } r)
        {
            _service.Skip(r);
            SavePrefs();
            Refresh();
        }
    }

    private async Task InstallAsync(ReleaseInfo release)
    {
        if (IsBusy)
        {
            return;
        }

        var older = release.Semver is { } v && SemVer.TryParse(_service.CurrentVersion) is { } cur && v < cur;
        var live = _shell.IsRunning
            ? "\n\nAhora mismo estás conectado: la conexión se cerrará y los efectos pendientes se perderán."
            : "";
        var verb = older ? $"Volver a la versión {release.Version} (anterior a la tuya)" : $"Instalar la versión {release.Version}";
        var ok = await _shell.Dialogs.ConfirmAsync("Actualizar HiveShock",
            $"{verb}.\n\nHiveShock se cerrará y se volverá a abrir solo en unos segundos. Tus perfiles, regalos, metas y credenciales no se tocan; " +
            $"si algo falla se restaura la versión actual.{live}").ConfigureAwait(true);
        if (!ok)
        {
            return;
        }

        IsBusy = true;
        try
        {
            if (_service.Downloaded?.Version != release.Version || _service.State != UpdateState.Ready)
            {
                if (!await _service.DownloadAsync(release).ConfigureAwait(true))
                {
                    return;
                }
            }

            if (!_service.DownloadVerified)
            {
                var go = await _shell.Dialogs.ConfirmAsync("Sin suma de verificación",
                    "Este release no publica una suma SHA-256, así que solo se pudo comprobar que el archivo está completo. ¿Instalar de todos modos?")
                    .ConfigureAwait(true);
                if (!go)
                {
                    return;
                }
            }

            if (!_service.BeginInstall(release))
            {
                return;
            }

            BridgeLog.Info($"Instalando HiveShock {release.Version}: se cerrará y se reabrirá solo.");
            await _shell.ShutdownAsync().ConfigureAwait(true);
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow?.Close();
            }
        }
        finally
        {
            IsBusy = false;
            Refresh();
        }
    }

    private void Refresh()
    {
        var s = _service;
        var latest = s.Latest;
        var announce = s.Announceable;

        ShowProgress = s.State == UpdateState.Downloading;
        ProgressPercent = s.Progress * 100;
        HasError = s.Error.Length > 0 && s.State == UpdateState.Failed;
        HasUpdate = announce is not null;
        BadgeText = announce is null ? "" : $"Nueva versión {announce.Version}";
        InstallLatestText = latest is null ? "" : $"Instalar {latest.Version}";

        (StatusText, StatusDetail) = s.State switch
        {
            UpdateState.Checking => ("Buscando actualizaciones…", ""),
            UpdateState.Downloading => ($"Descargando {s.Downloaded?.Version}… {s.Progress:P0}", "Puedes seguir usando HiveShock."),
            UpdateState.Ready when s.Downloaded is not null =>
                ($"La versión {s.Downloaded.Version} está lista para instalar", "Ya está descargada y verificada."),
            UpdateState.Applying => ("Instalando…", "HiveShock se cerrará y se volverá a abrir."),
            UpdateState.Failed => ("No se pudo completar", s.Error),
            _ when latest is not null => ($"Hay una versión nueva: {latest.Version}", latest.Prerelease ? "Es una versión de prueba (pre-release)." : ""),
            UpdateState.UpToDate => ("Estás al día", s.LastChecked is { } t ? $"Última comprobación: {t:g}" : ""),
            _ => ("Aún no se ha comprobado", "Pulsa «Buscar actualizaciones»."),
        };

        Rows.Clear();
        foreach (var r in s.Releases.Where(r => IncludePrereleases || !r.Prerelease || s.IsCurrent(r)))
        {
            Rows.Add(ToRow(r));
        }

        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(BadgeVisible));
        OnPropertyChanged(nameof(CurrentVersionText));
        _shell.NotifyUpdateBadge();

        // Un solo aviso en el panel de actividad por versión nueva.
        if (announce is not null && announce.Version != _announcedVersion)
        {
            _announcedVersion = announce.Version;
            BridgeLog.Info($"Hay una versión nueva de HiveShock: {announce.Version}. Instálala desde Actualizaciones.");
        }
    }

    private ReleaseRow ToRow(ReleaseInfo r)
    {
        var current = _service.IsCurrent(r);
        var newer = _service.IsNewer(r);
        var asset = UpdatePlatform.SelectAsset(r);
        var notes = r.Notes.Trim();
        if (notes.Length > 700)
        {
            notes = notes[..700].TrimEnd() + "…";
        }

        return new ReleaseRow
        {
            Release = r,
            Version = r.Version,
            Title = r.Title,
            DateText = r.PublishedAt is { } d ? d.LocalDateTime.ToString("d MMM yyyy") : "",
            Badge = current ? "Instalada" : newer ? "Más nueva" : "Anterior",
            BadgeTone = current ? "current" : newer ? "newer" : "older",
            IsPrerelease = r.Prerelease,
            Notes = notes,
            CanInstall = !current && asset is not null,
            ActionText = newer ? "Instalar" : "Volver a esta",
            Unavailable = !current && asset is null ? "Sin paquete para tu sistema" : "",
        };
    }
}
