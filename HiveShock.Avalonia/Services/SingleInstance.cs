using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace HiveShock.Avalonia.Services;

/// <summary>
/// Una sola copia de HiveShock por usuario. Con la X que deja la app en la bandeja es fácil volver a abrirla
/// sin darse cuenta de que ya está en marcha: dos puentes a la vez pelearían por los puertos del juego, los
/// overlays de OBS (7878) y los archivos de configuración, y podrían lanzar cada efecto dos veces.
///
/// La segunda copia no arranca: avisa a la primera por un canal con nombre (named pipe, que en macOS/Linux es
/// un socket de dominio Unix) para que vuelva a mostrar su ventana, y termina.
///
/// El actualizador (<c>--apply-update</c>) no pasa por aquí y espera a que el proceso viejo termine antes de
/// lanzar el nuevo, así que la versión recién instalada siempre encuentra el candado libre.
/// </summary>
public static class SingleInstance
{
    private const string ShowMessage = "show";

    private static Mutex? _mutex;
    private static CancellationTokenSource? _listenCts;

    private static string Id
    {
        get
        {
            // Por usuario: dos cuentas del mismo equipo pueden usar cada una su HiveShock.
            var raw = $"HiveShock|{Environment.UserDomainName}|{Environment.UserName}";
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..16];
            return $"HiveShock-{hash}";
        }
    }

    /// <summary>True si esta es la primera copia (y se queda con el candado hasta salir).</summary>
    public static bool TryAcquire()
    {
        try
        {
            // "Local\" = sesión del usuario en Windows; en Unix .NET usa el nombre tal cual.
            var name = OperatingSystem.IsWindows() ? $@"Local\{Id}" : Id;
            _mutex = new Mutex(initiallyOwned: true, name, out var createdNew);
            if (createdNew)
            {
                return true;
            }

            _mutex.Dispose();
            _mutex = null;
            return false;
        }
        catch (AbandonedMutexException)
        {
            // La copia anterior se cerró de golpe: el candado es nuestro.
            return true;
        }
        catch
        {
            // Si el sistema no deja crear el candado, mejor arrancar que impedir usar la app.
            return true;
        }
    }

    /// <summary>Pide a la copia que ya está abierta que muestre su ventana. False si no respondió.</summary>
    public static bool SignalExisting()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", Id, PipeDirection.Out);
            client.Connect(2000);
            var bytes = Encoding.UTF8.GetBytes(ShowMessage);
            client.Write(bytes, 0, bytes.Length);
            client.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Escucha avisos de otras copias en segundo plano; <paramref name="onShow"/> se llama fuera del hilo de UI.</summary>
    public static void Listen(Action onShow)
    {
        _listenCts = new CancellationTokenSource();
        var token = _listenCts.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(
                        Id, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                    var buffer = new byte[16];
                    var read = await server.ReadAsync(buffer, token).ConfigureAwait(false);
                    if (Encoding.UTF8.GetString(buffer, 0, read) == ShowMessage)
                    {
                        onShow();
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch
                {
                    // Un cliente que se va a medias no debe tumbar la escucha; reintenta tras una pausa.
                    try
                    {
                        await Task.Delay(500, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
        }, token);
    }

    public static void Release()
    {
        _listenCts?.Cancel();
        try
        {
            _mutex?.ReleaseMutex();
        }
        catch
        {
            // No era nuestro o ya se soltó.
        }

        _mutex?.Dispose();
        _mutex = null;
    }
}
