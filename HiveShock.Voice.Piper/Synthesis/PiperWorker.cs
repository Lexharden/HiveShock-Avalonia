using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using HiveShock.Logging;

namespace HiveShock.Voice.Piper.Synthesis;

/// <summary>
/// Piper no arranca porque a Windows le faltan librerías del sistema (normalmente el
/// "Microsoft Visual C++ Redistributable"). La UI lo reconoce y ofrece descargarlo.
/// </summary>
public sealed class PiperDependencyException(string message) : InvalidOperationException(message)
{
    /// <summary>Instalador oficial de Microsoft (Visual C++ 2015–2022, x64).</summary>
    public const string VisualCppRedistributableUrl = "https://aka.ms/vs/17/release/vc_redist.x64.exe";

    public const string DefaultMessage =
        "Las voces locales no pudieron arrancar porque faltan librerías. " +
        "1) Instala «Microsoft Visual C++ Redistributable (x64)» desde la web de Microsoft y reinicia HiveShock. " +
        "2) Si sigue fallando, pulsa «Borrar motor y voces» en «Voces locales HD» y vuelve a instalarlas (algún archivo pudo dañarse o borrarlo el antivirus).";

    /// <summary>
    /// Códigos con los que Windows cierra un programa que no puede cargar sus DLL:
    /// 0xC0000135 (no se encontró), 0xC000007B (formato/arquitectura incorrectos),
    /// 0xC0000142 (falló al inicializarse).
    /// </summary>
    public static bool IsMissingLibraryExitCode(int exitCode) =>
        unchecked((uint)exitCode) is 0xC0000135 or 0xC000007B or 0xC0000142;
}

/// <summary>Lo necesario para arrancar un proceso de Piper con una voz y una velocidad.</summary>
internal sealed record PiperWorkerOptions(
    string ExecutablePath,
    string EspeakDataPath,
    string ModelPath,
    string ConfigPath,
    double LengthScale,
    string TempDirectory)
{
    /// <summary>Un proceso por voz y velocidad: Piper no admite cambiar la velocidad por frase.</summary>
    public string Key => $"{ModelPath}|{LengthScale.ToString("0.00", CultureInfo.InvariantCulture)}";
}

/// <summary>
/// Un piper.exe vivo con una voz cargada. Protocolo (verificado en la Fase 0): una línea JSON
/// por frase en stdin ({"text", "output_file", "speaker_id"}), Piper escribe el WAV y responde
/// con la ruta en stdout. Así el modelo se carga una vez (0,3–0,7 s) y cada frase tarda
/// ~250 ms. stdin va en UTF-8 sin BOM (con BOM o ANSI se estropean acentos y ñ). Las frases
/// van de una en una; si el proceso se cuelga o muere, se mata y se arranca otro.
/// </summary>
internal sealed class PiperWorker : IDisposable
{
    private static readonly TimeSpan PhraseTimeout = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Queue<string> _recentErrors = new();
    private Process? _process;
    private bool _disposed;

    public PiperWorker(PiperWorkerOptions options)
    {
        Options = options;
    }

    public PiperWorkerOptions Options { get; }

    public DateTime LastUsedUtc { get; private set; } = DateTime.UtcNow;

    /// <summary>Marca el proceso como recién usado (el pool cierra primero el que lleva más sin usarse).</summary>
    public void Touch() => LastUsedUtc = DateTime.UtcNow;

    public async Task<byte[]> SynthesizeAsync(string text, int? speakerId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            LastUsedUtc = DateTime.UtcNow;
            var process = EnsureStarted();

            Directory.CreateDirectory(Options.TempDirectory);
            var output = Path.Combine(Options.TempDirectory, $"{Guid.NewGuid():N}.wav");
            var request = speakerId is { } speaker
                ? JsonSerializer.Serialize(new { text, output_file = output, speaker_id = speaker })
                : JsonSerializer.Serialize(new { text, output_file = output });

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(PhraseTimeout);
            try
            {
                await process.StandardInput.WriteLineAsync(request.AsMemory(), timeout.Token).ConfigureAwait(false);
                await process.StandardInput.FlushAsync(timeout.Token).ConfigureAwait(false);
                var answer = await process.StandardOutput.ReadLineAsync(timeout.Token).ConfigureAwait(false);
                if (answer == null)
                {
                    ThrowIfMissingLibraries(process);
                    throw new InvalidOperationException($"El motor de voces locales se cerró inesperadamente. {LastError()}");
                }

                var audio = await File.ReadAllBytesAsync(output, ct).ConfigureAwait(false);
                if (audio.Length <= 44)
                {
                    throw new InvalidOperationException("El motor de voces locales no generó audio para ese texto.");
                }

                return audio;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                Kill();
                throw new TimeoutException("La voz local tardó demasiado en responder. Se reiniciará con el siguiente mensaje.");
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException && ex is not PiperDependencyException && process.HasExited)
            {
                ThrowIfMissingLibraries(process);
                Kill();
                throw new InvalidOperationException($"El motor de voces locales se detuvo. {LastError()}", ex);
            }
            catch (OperationCanceledException)
            {
                // Cancelado a mitad de una frase: la respuesta pendiente desincronizaría el protocolo.
                Kill();
                throw;
            }
            finally
            {
                TryDelete(output);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private Process EnsureStarted()
    {
        if (_process is { HasExited: false } alive)
        {
            return alive;
        }

        Kill();
        var psi = new ProcessStartInfo(Options.ExecutablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(Options.ExecutablePath)!,
        };
        // ArgumentList: sin shell ni comillas a mano, así ninguna ruta o texto puede inyectar comandos.
        foreach (var arg in new[]
                 {
                     "--model", Options.ModelPath,
                     "--config", Options.ConfigPath,
                     "--espeak_data", Options.EspeakDataPath,
                     "--json-input",
                     "--output_dir", Options.TempDirectory,
                     "--length_scale", Options.LengthScale.ToString("0.00", CultureInfo.InvariantCulture),
                     "--quiet",
                 })
        {
            psi.ArgumentList.Add(arg);
        }

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.ErrorDataReceived += (_, e) => RememberError(e.Data);
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("No se pudo iniciar el motor de voces locales.");
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            process.Dispose();
            // Típico: el antivirus bloqueó piper.exe o el archivo se borró.
            throw new InvalidOperationException(
                "Windows no dejó iniciar el motor de voces locales. Si tu antivirus lo bloqueó, permítelo o reinstálalo desde «Voces locales HD».", ex);
        }

        ChildProcessGuard.Attach(process);
        process.BeginErrorReadLine();
        _process = process;
        BridgeLog.Info($"Piper: voz {Path.GetFileNameWithoutExtension(Options.ModelPath)} cargada (velocidad x{1 / Options.LengthScale:0.00})");
        return process;
    }

    /// <summary>Si Piper murió por falta de DLL del sistema, lo traduce a <see cref="PiperDependencyException"/>.</summary>
    private void ThrowIfMissingLibraries(Process process)
    {
        try
        {
            if (!process.WaitForExit(2000) || !PiperDependencyException.IsMissingLibraryExitCode(process.ExitCode))
            {
                return;
            }
        }
        catch (InvalidOperationException)
        {
            return; // el proceso ya se liberó
        }

        BridgeLog.Error($"Piper: faltan librerías del sistema (código 0x{unchecked((uint)process.ExitCode):X8}).");
        Kill();
        throw new PiperDependencyException(PiperDependencyException.DefaultMessage);
    }

    private void RememberError(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        lock (_recentErrors)
        {
            _recentErrors.Enqueue(line.Trim());
            while (_recentErrors.Count > 5)
            {
                _recentErrors.Dequeue();
            }
        }
    }

    private string LastError()
    {
        lock (_recentErrors)
        {
            return _recentErrors.Count == 0 ? "" : $"Detalle: {_recentErrors.Last()}";
        }
    }

    private void Kill()
    {
        var process = _process;
        _process = null;
        if (process == null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // ya terminó
        }

        process.Dispose();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // WAV temporal: se limpia con la carpeta tmp
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _process?.StandardInput.Close(); // fin de stdin = Piper termina por su cuenta
            _process?.WaitForExit(1000);
        }
        catch
        {
            // se mata abajo
        }

        Kill();
        _gate.Dispose();
    }
}
