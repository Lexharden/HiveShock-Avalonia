using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HiveShock.Logging;
using HiveShock.Voice;
using HiveShock.Voice.Piper;
using HiveShock.Voice.Piper.Runtime;
using HiveShock.Voice.Piper.Synthesis;
using HiveShock.Voice.Piper.Voices;

namespace HiveShock.Avalonia.ViewModels;

/// <summary>
/// Tarjeta "Voces locales HD": instala el motor Piper (una vez), muestra el catálogo oficial
/// agrupado por idioma y descarga/borra voces con progreso. Todo en lenguaje de streamer:
/// tamaños en MB, calidad en palabras y la licencia de cada voz a un clic.
/// </summary>
public sealed partial class PiperManagerViewModel : ViewModelBase
{
    private readonly PiperTtsEngine _engine;
    private readonly Action _enginesChanged;
    private readonly Func<string, string, Task<bool>> _confirm;
    private IReadOnlyList<PiperVoiceInfo> _catalog = [];
    private CancellationTokenSource? _runtimeCts;

    public PiperManagerViewModel(PiperTtsEngine engine, Action enginesChanged, Func<string, string, Task<bool>> confirm)
    {
        _engine = engine;
        _enginesChanged = enginesChanged;
        _confirm = confirm;
        _engine.Store.Changed += () => Dispatcher.UIThread.Post(OnStoreChanged);
        _engine.Runtime.Changed += () => Dispatcher.UIThread.Post(OnRuntimeChanged);
    }

    public bool IsSupported => _engine.Runtime.IsSupported;
    public bool IsRuntimeInstalled => _engine.Runtime.IsInstalled;
    public bool ShowInstallRuntime => IsSupported && !IsRuntimeInstalled;

    public string InstallRuntimeText =>
        $"Instalar voces locales HD (≈ {PiperRuntime.CurrentAsset?.ApproxMegabytes ?? 22} MB)";

    [ObservableProperty] private bool _isInstallingRuntime;
    [ObservableProperty] private double _runtimeProgress;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _errorText = "";
    [ObservableProperty] private bool _isLoadingCatalog;
    [ObservableProperty] private bool _onlySpanish = true;
    [ObservableProperty] private string _searchText = "";

    public ObservableCollection<PiperCatalogRow> Rows { get; } = [];

    /// <summary>Voces añadidas a mano (pares .onnx + .onnx.json en la carpeta de voces).</summary>
    public ObservableCollection<PiperCustomVoiceRow> CustomVoices { get; } = [];

    public bool HasCustomVoices => CustomVoices.Count > 0;

    public string VoicesFolder => _engine.Paths.VoicesDirectory;

    /// <summary>La última prueba falló por librerías de Windows: se muestra el botón de Visual C++.</summary>
    [ObservableProperty] private bool _showDependencyHelp;

    [ObservableProperty] private bool _isTesting;

    public string InstalledSummary
    {
        get
        {
            var count = _engine.Store.Installed().Count;
            var mb = _engine.DiskUsageBytes() / 1048576.0;
            return count switch
            {
                0 => $"Ninguna voz descargada todavía · {mb:0} MB en disco",
                1 => $"1 voz descargada · {mb:0} MB en disco",
                _ => $"{count} voces descargadas · {mb:0} MB en disco",
            };
        }
    }

    partial void OnOnlySpanishChanged(bool value) => RebuildRows();

    partial void OnSearchTextChanged(string value) => RebuildRows();

    /// <summary>Se llama al abrir la página Voz: si el motor ya está, carga el catálogo (de la copia local si no hay internet).</summary>
    public async Task InitializeAsync()
    {
        RebuildCustomVoices();
        if (IsRuntimeInstalled && _catalog.Count == 0)
        {
            await LoadCatalogAsync(false).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task InstallRuntime()
    {
        if (IsInstallingRuntime)
        {
            return;
        }

        ErrorText = "";
        IsInstallingRuntime = true;
        RuntimeProgress = 0;
        StatusText = "Descargando el motor de voces locales…";
        _runtimeCts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<DownloadProgress>(p => RuntimeProgress = (p.Fraction ?? 0) * 100);
            await _engine.Runtime.InstallAsync(progress, _runtimeCts.Token).ConfigureAwait(true);
            StatusText = "Motor instalado. Ahora descarga al menos una voz.";
            await LoadCatalogAsync(false).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Instalación cancelada.";
        }
        catch (Exception ex)
        {
            StatusText = "";
            ErrorText = Friendly(ex, "No se pudo instalar el motor de voces locales.");
            BridgeLog.Warn($"Piper instalación: {ex.Message}");
        }
        finally
        {
            IsInstallingRuntime = false;
            _runtimeCts?.Dispose();
            _runtimeCts = null;
            OnRuntimeChanged();
        }
    }

    [RelayCommand]
    private void CancelInstallRuntime() => _runtimeCts?.Cancel();

    [RelayCommand]
    private void OpenVoicesFolder()
    {
        try
        {
            Directory.CreateDirectory(VoicesFolder);
            PlatformShell.OpenFolder(VoicesFolder);
        }
        catch (Exception ex)
        {
            ErrorText = Friendly(ex, "No se pudo abrir la carpeta de voces.");
        }
    }

    /// <summary>Tras copiar voces propias a la carpeta: las busca sin reiniciar HiveShock.</summary>
    [RelayCommand]
    private void RescanVoices()
    {
        ErrorText = "";
        _engine.Store.Refresh();
        var custom = _engine.Store.Installed().Count(v => v.IsCustom);
        StatusText = custom switch
        {
            0 => "No se encontraron voces propias. Revisa que estén los dos archivos (nombre.onnx y nombre.onnx.json).",
            1 => "Se encontró 1 voz propia. Ya aparece en la lista de voces.",
            _ => $"Se encontraron {custom} voces propias. Ya aparecen en la lista de voces.",
        };
    }

    /// <summary>Sintetiza una frase en silencio con la primera voz: confirma que motor y librerías funcionan.</summary>
    [RelayCommand]
    private async Task TestEngine()
    {
        if (IsTesting)
        {
            return;
        }

        ErrorText = "";
        ShowDependencyHelp = false;
        if (_engine.Store.Installed().Count == 0)
        {
            ErrorText = "Descarga o añade al menos una voz para poder probar.";
            return;
        }

        IsTesting = true;
        StatusText = "Probando las voces locales…";
        try
        {
            await _engine.CheckAsync("", CancellationToken.None).ConfigureAwait(true);
            StatusText = "✓ Las voces locales funcionan correctamente.";
        }
        catch (PiperDependencyException ex)
        {
            StatusText = "";
            ErrorText = ex.Message;
            ShowDependencyHelp = true;
        }
        catch (Exception ex)
        {
            StatusText = "";
            ErrorText = Friendly(ex, "Las voces locales no respondieron.");
            ShowDependencyHelp = true;
        }
        finally
        {
            IsTesting = false;
        }
    }

    [RelayCommand]
    private static void OpenVisualCppDownload() => PlatformShell.OpenUrl(PiperDependencyException.VisualCppRedistributableUrl);

    [RelayCommand]
    private Task RefreshCatalog() => LoadCatalogAsync(true);

    [RelayCommand]
    private async Task UninstallAll()
    {
        if (!await _confirm("Borrar voces locales",
                "Se borrarán el motor de voces locales y todas las voces: las descargadas y las que añadiste a mano. Podrás volver a instalarlas cuando quieras.\n\n¿Continuar?")
                .ConfigureAwait(true))
        {
            return;
        }

        try
        {
            _engine.UninstallAll();
            StatusText = "Voces locales borradas.";
        }
        catch (Exception ex)
        {
            ErrorText = Friendly(ex, "No se pudo borrar todo. Cierra HiveShock y vuelve a intentarlo.");
        }
    }

    internal async Task DownloadAsync(PiperCatalogRow row)
    {
        ErrorText = "";
        row.StartDownload();
        try
        {
            var progress = new Progress<DownloadProgress>(p => row.Progress = (p.Fraction ?? 0) * 100);
            var firstVoice = _engine.Store.Installed().Count == 0;
            await _engine.Store.InstallAsync(row.Info, progress, row.DownloadToken).ConfigureAwait(true);
            StatusText = $"Voz {row.Info.DisplayName} lista.";
            if (firstVoice)
            {
                // Primera voz: se prueba ya, así un problema de librerías aparece aquí y no en pleno directo.
                await TestEngine().ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Descarga cancelada.";
        }
        catch (Exception ex)
        {
            ErrorText = Friendly(ex, $"No se pudo descargar la voz {row.Info.DisplayName}.");
            BridgeLog.Warn($"Piper voz {row.Info.Key}: {ex.Message}");
        }
        finally
        {
            row.EndDownload();
            row.IsInstalled = _engine.Store.IsInstalled(row.Info.Key);
        }
    }

    internal async Task DeleteCustomAsync(PiperCustomVoiceRow row)
    {
        if (!await _confirm("Borrar voz propia", $"¿Borrar la voz {row.Name}? Se eliminarán sus dos archivos de la carpeta de voces.")
                .ConfigureAwait(true))
        {
            return;
        }

        try
        {
            _engine.Store.Delete(row.Key);
        }
        catch (Exception ex)
        {
            ErrorText = Friendly(ex, "No se pudo borrar la voz. Puede estar en uso: inténtalo en unos segundos.");
        }
    }

    internal async Task DeleteAsync(PiperCatalogRow row)
    {
        if (!await _confirm("Borrar voz", $"¿Borrar la voz {row.Info.DisplayName} ({row.SizeText})?").ConfigureAwait(true))
        {
            return;
        }

        try
        {
            _engine.Store.Delete(row.Info.Key);
            row.IsInstalled = false;
        }
        catch (Exception ex)
        {
            ErrorText = Friendly(ex, "No se pudo borrar la voz. Puede estar en uso: inténtalo en unos segundos.");
        }
    }

    internal static void OpenLicense(PiperCatalogRow row)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = row.Info.LicenseUrl, UseShellExecute = true });
        }
        catch
        {
            // sin navegador predeterminado: nada que hacer
        }
    }

    private async Task LoadCatalogAsync(bool refresh)
    {
        if (IsLoadingCatalog)
        {
            return;
        }

        IsLoadingCatalog = true;
        ErrorText = "";
        try
        {
            _catalog = await _engine.Catalog.LoadAsync(refresh, CancellationToken.None).ConfigureAwait(true);
            RebuildRows();
        }
        catch (Exception ex)
        {
            ErrorText = Friendly(ex, "No se pudo cargar la lista de voces locales.");
        }
        finally
        {
            IsLoadingCatalog = false;
        }
    }

    private void RebuildRows()
    {
        var search = SearchText.Trim();
        var installed = _engine.Store.Installed().Select(v => v.Info.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var downloading = Rows.Where(r => r.IsDownloading).ToDictionary(r => r.Info.Key, StringComparer.OrdinalIgnoreCase);

        var voices = _catalog
            .Where(v => !OnlySpanish || v.LanguageCode.StartsWith("es", StringComparison.OrdinalIgnoreCase))
            .Select(v => (Voice: v, Code: VoiceLanguages.LanguageCode(v.Locale)))
            .Where(x => search.Length == 0 ||
                        x.Voice.DisplayName.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                        VoiceLanguages.LanguageName(x.Code).Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                        VoiceLanguages.RegionName(x.Voice.Locale).Contains(search, StringComparison.CurrentCultureIgnoreCase));

        RebuildCustomVoices();
        Rows.Clear();
        foreach (var group in voices
                     .GroupBy(x => x.Code)
                     .OrderBy(g => VoiceLanguages.SortKey(g.Key))
                     .ThenBy(g => VoiceLanguages.LanguageName(g.Key), StringComparer.CurrentCultureIgnoreCase))
        {
            Rows.Add(PiperCatalogRow.Header(VoiceLanguages.LanguageName(group.Key)));
            foreach (var (voice, _) in group
                         .OrderBy(x => VoiceLanguages.RegionName(x.Voice.Locale), StringComparer.CurrentCultureIgnoreCase)
                         .ThenBy(x => x.Voice.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                         .ThenBy(x => x.Voice.SizeBytes))
            {
                // Una descarga en curso sobrevive a cambiar el filtro.
                Rows.Add(downloading.TryGetValue(voice.Key, out var active)
                    ? active
                    : new PiperCatalogRow(this, voice) { IsInstalled = installed.Contains(voice.Key) });
            }
        }

        OnPropertyChanged(nameof(InstalledSummary));
    }

    private void OnStoreChanged()
    {
        foreach (var row in Rows.Where(r => !r.IsHeader))
        {
            row.IsInstalled = _engine.Store.IsInstalled(row.Info.Key);
        }

        RebuildCustomVoices();

        OnPropertyChanged(nameof(InstalledSummary));
        _enginesChanged();
    }

    private void RebuildCustomVoices()
    {
        CustomVoices.Clear();
        foreach (var voice in _engine.Store.Installed().Where(v => v.IsCustom))
        {
            CustomVoices.Add(new PiperCustomVoiceRow(this, voice));
        }

        OnPropertyChanged(nameof(HasCustomVoices));
    }

    private void OnRuntimeChanged()
    {
        OnPropertyChanged(nameof(IsRuntimeInstalled));
        OnPropertyChanged(nameof(ShowInstallRuntime));
        OnPropertyChanged(nameof(InstalledSummary));
        if (!IsRuntimeInstalled)
        {
            _catalog = [];
            Rows.Clear();
        }

        _enginesChanged();
    }

    private static string Friendly(Exception ex, string fallback) => ex switch
    {
        InvalidOperationException or PlatformNotSupportedException => ex.Message,
        System.Net.Http.HttpRequestException or IOException when ex is not FileNotFoundException =>
            $"{fallback} Revisa tu internet y el espacio libre en disco.",
        UnauthorizedAccessException => $"{fallback} Windows no dejó escribir en la carpeta de datos.",
        _ => fallback,
    };
}

/// <summary>Fila del catálogo: cabecera de idioma o una voz con su estado de descarga.</summary>
public sealed partial class PiperCatalogRow : ObservableObject
{
    private readonly PiperManagerViewModel? _owner;
    private CancellationTokenSource? _downloadCts;

    private PiperCatalogRow(string header)
    {
        HeaderText = header;
        Info = null!;
    }

    public PiperCatalogRow(PiperManagerViewModel owner, PiperVoiceInfo info)
    {
        _owner = owner;
        Info = info;
        HeaderText = "";
    }

    public static PiperCatalogRow Header(string text) => new(text);

    public bool IsHeader => _owner == null;
    public string HeaderText { get; }
    public PiperVoiceInfo Info { get; }

    public string Title => IsHeader ? HeaderText :
        VoiceLanguages.RegionName(Info.Locale) is { Length: > 0 } region ? $"{Info.DisplayName} · {region}" : Info.DisplayName;

    public string Details => IsHeader ? "" :
        $"{Info.QualityLabel} · {SizeText}{(Info.NumSpeakers > 1 ? $" · {Info.NumSpeakers} voces en una" : "")}";

    public string SizeText => IsHeader ? "" : $"{Info.SizeBytes / 1048576.0:0} MB";

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanDownload))] private bool _isInstalled;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanDownload))] private bool _isDownloading;
    [ObservableProperty] private double _progress;

    public bool CanDownload => !IsHeader && !IsInstalled && !IsDownloading;

    internal CancellationToken DownloadToken => _downloadCts?.Token ?? CancellationToken.None;

    internal void StartDownload()
    {
        _downloadCts = new CancellationTokenSource();
        Progress = 0;
        IsDownloading = true;
    }

    internal void EndDownload()
    {
        IsDownloading = false;
        _downloadCts?.Dispose();
        _downloadCts = null;
    }

    [RelayCommand]
    private Task Download() => _owner?.DownloadAsync(this) ?? Task.CompletedTask;

    [RelayCommand]
    private void CancelDownload() => _downloadCts?.Cancel();

    [RelayCommand]
    private Task Delete() => _owner?.DeleteAsync(this) ?? Task.CompletedTask;

    [RelayCommand]
    private void OpenLicense() => PiperManagerViewModel.OpenLicense(this);
}

/// <summary>Voz añadida a mano: nombre, idioma leído de su .onnx.json y botón de borrar.</summary>
public sealed partial class PiperCustomVoiceRow
{
    private readonly PiperManagerViewModel _owner;

    public PiperCustomVoiceRow(PiperManagerViewModel owner, PiperInstalledVoice voice)
    {
        _owner = owner;
        Key = voice.Info.Key;
        Name = voice.Info.DisplayName;
        var code = VoiceLanguages.LanguageCode(voice.Info.Locale);
        var region = VoiceLanguages.RegionName(voice.Info.Locale);
        Details = code.Length == 0
            ? "Idioma desconocido (su .onnx.json no lo indica)"
            : region.Length > 0 ? $"{VoiceLanguages.LanguageName(code)} ({region})" : VoiceLanguages.LanguageName(code);
        FileName = Path.GetFileName(voice.ModelPath);
    }

    public string Key { get; }
    public string Name { get; }
    public string Details { get; }
    public string FileName { get; }

    [RelayCommand]
    private Task Delete() => _owner.DeleteCustomAsync(this);
}
