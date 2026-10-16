using System.Text.Json;
using HiveShock.Configuration;

namespace HiveShock.Updates;

public enum UpdateState { Idle, Checking, UpToDate, Available, Downloading, Ready, Applying, Failed }

/// <summary>Opciones que el usuario controla; la UI las mantiene sincronizadas con sus preferencias.</summary>
public sealed class UpdateOptions
{
    public bool CheckOnStart { get; set; } = true;
    public bool IncludePrereleases { get; set; }
    public string SkippedVersion { get; set; } = "";
}

/// <summary>
/// Comprueba los Releases de GitHub y anuncia la versión nueva. No descarga ni instala nada por su cuenta:
/// descargar e instalar son siempre acciones del usuario.
/// </summary>
public sealed class UpdateService : IDisposable
{
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly GitHubReleaseClient _client;
    private readonly UpdateDownloader _downloader;
    private readonly UpdatePaths _paths;
    private readonly string _appDir;
    private readonly SemVer _current;
    private readonly string _rid;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _cts;
    private Timer? _timer;
    private string? _etag;

    public UpdateService(
        HttpClient http,
        string currentVersion,
        UpdateOptions options,
        UpdatePaths? paths = null,
        string? appDir = null,
        string? rid = null,
        GitHubReleaseClient? client = null)
    {
        _current = SemVer.TryParse(currentVersion) ?? SemVer.TryParse("0.0.0")!;
        CurrentVersion = _current.ToString();
        Options = options;
        _paths = paths ?? UpdatePaths.Default();
        _appDir = appDir ?? AppPaths.AppDirectory;
        _rid = rid ?? UpdatePlatform.CurrentRid();
        _client = client ?? GitHubReleaseClient.FromEnvironment(http);
        _downloader = new UpdateDownloader(http);
        LoadCache();
    }

    public string CurrentVersion { get; }
    public UpdateOptions Options { get; }
    public UpdatePaths Paths => _paths;

    public UpdateState State { get; private set; } = UpdateState.Idle;
    public string Error { get; private set; } = "";
    public double Progress { get; private set; }
    public IReadOnlyList<ReleaseInfo> Releases { get; private set; } = [];
    public DateTimeOffset? LastChecked { get; private set; }

    /// <summary>Versión en descarga o ya descargada y lista para instalar.</summary>
    public ReleaseInfo? Downloaded { get; private set; }

    /// <summary>Última versión ofrecida más nueva que la instalada (respeta el canal elegido), o null.</summary>
    public ReleaseInfo? Latest =>
        Releases.Where(r => IsNewer(r) && (Options.IncludePrereleases || !r.Prerelease)
                            && UpdatePlatform.SelectAsset(r, _rid) is not null)
            .OrderByDescending(static r => r.Semver)
            .FirstOrDefault();

    /// <summary>Hay una versión nueva que el usuario no ha omitido.</summary>
    public ReleaseInfo? Announceable =>
        Latest is { } l && !string.Equals(l.Version, Options.SkippedVersion, StringComparison.OrdinalIgnoreCase) ? l : null;

    /// <summary>Se dispara en cualquier hilo cuando cambia el estado, el progreso o la lista.</summary>
    public event Action? Changed;

    public bool IsNewer(ReleaseInfo r) => r.Semver is { } v && v > _current;
    public bool IsCurrent(ReleaseInfo r) => r.Semver is { } v && v.Equals(_current);

    /// <summary>Arranca las comprobaciones periódicas (la primera, tras una pequeña espera).</summary>
    public void Start(TimeSpan? firstDelay = null)
    {
        if (_timer is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _timer = new Timer(async _ =>
        {
            try
            {
                if (Options.CheckOnStart)
                {
                    await CheckAsync(silent: true).ConfigureAwait(false);
                }
            }
            catch
            {
                // una comprobación periódica nunca debe tumbar la app
            }
        }, null, firstDelay ?? TimeSpan.FromSeconds(15), CheckInterval);
    }

    public async Task CheckAsync(bool silent = false, CancellationToken ct = default)
    {
        if (!await _gate.WaitAsync(0, ct).ConfigureAwait(false))
        {
            return; // ya hay una operación en curso
        }

        try
        {
            if (State is UpdateState.Downloading or UpdateState.Applying)
            {
                return;
            }

            SetState(UpdateState.Checking, silent ? null : "");
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                var fetched = await _client.FetchAsync(_etag, timeout.Token).ConfigureAwait(false);
                if (!fetched.NotModified)
                {
                    Releases = fetched.Releases;
                    _etag = fetched.ETag;
                    SaveCache();
                }

                LastChecked = DateTimeOffset.Now;
                SetState(Latest is null ? UpdateState.UpToDate : UpdateState.Available, "");
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                SetState(silent ? RestingState() : UpdateState.Failed, silent ? "" : "GitHub tardó demasiado en responder.");
            }
            catch (UpdateException ex)
            {
                // En silencio no se molesta al usuario por estar sin conexión.
                SetState(silent ? RestingState() : UpdateState.Failed, silent ? "" : ex.Message);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Descarga y verifica el zip de una versión concreta (la última o cualquier otra publicada).</summary>
    public async Task<bool> DownloadAsync(ReleaseInfo release, bool silent = false, CancellationToken ct = default)
    {
        if (!await _gate.WaitAsync(0, ct).ConfigureAwait(false))
        {
            return false;
        }

        try
        {
            var asset = UpdatePlatform.SelectAsset(release, _rid);
            if (asset is null)
            {
                SetState(RestingState(), $"La versión {release.Version} no tiene paquete para tu sistema ({_rid}).");
                return false;
            }

            Downloaded = release;
            Progress = 0;
            SetState(UpdateState.Downloading, "");
            var progress = new Progress<double>(p =>
            {
                Progress = p;
                Changed?.Invoke();
            });

            try
            {
                var result = await _downloader.DownloadAsync(release, asset, _paths.Downloads(release.Version), progress, ct)
                    .ConfigureAwait(false);
                DownloadVerified = result.Verified;
                SetState(UpdateState.Ready, "");
                return true;
            }
            catch (UpdateException ex)
            {
                Downloaded = null;
                SetState(silent ? RestingState() : UpdateState.Failed, silent ? "" : ex.Message);
                return false;
            }
            catch (OperationCanceledException)
            {
                Downloaded = null;
                SetState(RestingState(), "");
                return false;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>False si la descarga lista no pudo comprobarse con una suma SHA-256 publicada.</summary>
    public bool DownloadVerified { get; private set; } = true;

    /// <summary>
    /// Extrae la versión descargada y lanza el ayudante. Si devuelve true, el llamador debe cerrar HiveShock ya
    /// (el ayudante espera a que termine, reemplaza los archivos y vuelve a abrirlo).
    /// </summary>
    public bool BeginInstall(ReleaseInfo release)
    {
        try
        {
            var asset = UpdatePlatform.SelectAsset(release, _rid)
                        ?? throw new UpdateException("No hay paquete para tu sistema.");
            var zip = Path.Combine(_paths.Downloads(release.Version), asset.Name);
            if (!File.Exists(zip))
            {
                throw new UpdateException("Falta el archivo descargado; vuelve a descargarlo.");
            }

            SetState(UpdateState.Applying, "");
            var layout = UpdateInstaller.Stage(zip, _paths, _appDir);
            UpdateInstaller.LaunchApplier(layout, _paths, CurrentVersion, release.Version);
            return true;
        }
        catch (Exception ex) when (ex is UpdateException or IOException or UnauthorizedAccessException
                                       or System.ComponentModel.Win32Exception or InvalidDataException)
        {
            SetState(RestingState(), ex.Message);
            State = UpdateState.Failed;
            Changed?.Invoke();
            return false;
        }
    }

    public void Skip(ReleaseInfo release)
    {
        Options.SkippedVersion = release.Version;
        Changed?.Invoke();
    }

    private UpdateState RestingState() => Latest is null ? UpdateState.UpToDate : UpdateState.Available;

    private void SetState(UpdateState state, string? error)
    {
        State = state;
        if (error is not null)
        {
            Error = error;
        }

        Changed?.Invoke();
    }

    private sealed class CacheFile
    {
        public string? ETag { get; set; }
        public List<ReleaseInfo> Releases { get; set; } = [];
    }

    // La lista sobrevive entre arranques: se ve sin conexión y el ETag evita gastar cuota de GitHub.
    private void LoadCache()
    {
        try
        {
            if (!File.Exists(_paths.CacheFile))
            {
                return;
            }

            var cache = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(_paths.CacheFile), Json);
            if (cache is null)
            {
                return;
            }

            Releases = cache.Releases.Where(static r => r.Semver is not null).OrderByDescending(static r => r.Semver).ToList();
            _etag = cache.ETag;
        }
        catch
        {
            // caché ilegible: se ignora
        }
    }

    private void SaveCache()
    {
        try
        {
            Directory.CreateDirectory(_paths.Root);
            File.WriteAllText(_paths.CacheFile,
                JsonSerializer.Serialize(new CacheFile { ETag = _etag, Releases = Releases.ToList() }, Json));
        }
        catch
        {
            // ignore
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _cts?.Cancel();
        _cts?.Dispose();
        _gate.Dispose();
    }
}
