using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HiveShock.Input;
using HiveShock.Logging;

namespace HiveShock.Platform.Windows;

/// <summary>
/// Atajos globales con <c>RegisterHotKey</c>. Los registros pertenecen al hilo que los crea y los WM_HOTKEY llegan a su
/// cola de mensajes, así que todo vive en un hilo propio con su bucle de mensajes. Sin hooks de teclado ni librerías nativas.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class Win32Hotkeys : IGlobalHotkeys
{
    private const uint WmHotkey = 0x0312;
    private const uint WmApp = 0x8000;
    private const uint WmQuit = 0x0012;
    private const uint ModNoRepeat = 0x4000;
    private const int ErrorHotkeyAlreadyRegistered = 1409;

    private readonly object _gate = new();
    private readonly ConcurrentQueue<Command> _commands = new();
    private readonly ManualResetEventSlim _started = new();
    private Thread? _thread;
    private uint _threadId;
    private bool _disposed;

    public IReadOnlyList<HotkeyFailure> Apply(IReadOnlyList<HotkeyBinding> bindings)
    {
        EnsureThread();
        var done = new TaskCompletionSource<IReadOnlyList<HotkeyFailure>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _commands.Enqueue(new Command(bindings, done));
        if (!NativeMethods.PostThreadMessageW(_threadId, WmApp, 0, 0))
        {
            return [.. bindings.Select(b => new HotkeyFailure(b.Id, "No se pudo contactar con el hilo de atajos."))];
        }

        return done.Task.Wait(TimeSpan.FromSeconds(3))
            ? done.Task.Result
            : [.. bindings.Select(b => new HotkeyFailure(b.Id, "Los atajos no respondieron a tiempo."))];
    }

    public void Dispose()
    {
        Thread? thread;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            thread = _thread;
        }

        if (thread != null)
        {
            NativeMethods.PostThreadMessageW(_threadId, WmQuit, 0, 0);
            thread.Join(TimeSpan.FromSeconds(2));
        }
    }

    private void EnsureThread()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_thread != null)
            {
                return;
            }

            _thread = new Thread(Loop) { IsBackground = true, Name = "HiveShock.Hotkeys" };
            _thread.Start();
        }

        _started.Wait();
    }

    private void Loop()
    {
        // Una llamada a PeekMessage crea la cola de mensajes del hilo; hasta entonces PostThreadMessage falla.
        NativeMethods.PeekMessageW(out _, 0, 0, 0, 0);
        _threadId = NativeMethods.GetCurrentThreadId();
        _started.Set();

        var handlers = new Dictionary<int, Action>();
        try
        {
            while (NativeMethods.GetMessageW(out var msg, 0, 0, 0) > 0)
            {
                if (msg.message == WmHotkey && handlers.TryGetValue((int)msg.wParam, out var action))
                {
                    // Fuera del bucle de mensajes: lo que haga la acción no debe retrasar el siguiente atajo.
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            action();
                        }
                        catch (Exception ex)
                        {
                            BridgeLog.Error($"Atajo global: {ex.Message}");
                        }
                    });
                }
                else if (msg.message == WmApp)
                {
                    while (_commands.TryDequeue(out var cmd))
                    {
                        cmd.Done.TrySetResult(Register(handlers, cmd.Bindings));
                    }
                }
            }
        }
        finally
        {
            Unregister(handlers);
        }
    }

    private static List<HotkeyFailure> Register(Dictionary<int, Action> handlers, IReadOnlyList<HotkeyBinding> bindings)
    {
        Unregister(handlers);
        var failures = new List<HotkeyFailure>();
        var id = 1;
        foreach (var b in bindings)
        {
            if (NativeMethods.RegisterHotKey(0, id, b.Combo.Modifiers | ModNoRepeat, b.Combo.VirtualKey))
            {
                handlers[id++] = b.OnPressed;
                continue;
            }

            var error = Marshal.GetLastWin32Error();
            failures.Add(new HotkeyFailure(
                b.Id,
                error == ErrorHotkeyAlreadyRegistered
                    ? $"{b.Combo.Text} ya la usa otra aplicación."
                    : $"{b.Combo.Text}: {new Win32Exception(error).Message}"));
        }

        return failures;
    }

    private static void Unregister(Dictionary<int, Action> handlers)
    {
        foreach (var id in handlers.Keys)
        {
            NativeMethods.UnregisterHotKey(0, id);
        }

        handlers.Clear();
    }

    private sealed record Command(IReadOnlyList<HotkeyBinding> Bindings, TaskCompletionSource<IReadOnlyList<HotkeyFailure>> Done);

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct MSG
        {
            public nint hwnd;
            public uint message;
            public nuint wParam;
            public nint lParam;
            public uint time;
            public int ptX;
            public int ptY;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint vk);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterHotKey(nint hwnd, int id);

        [DllImport("user32.dll")]
        public static extern int GetMessageW(out MSG msg, nint hwnd, uint min, uint max);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PeekMessageW(out MSG msg, nint hwnd, uint min, uint max, uint remove);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostThreadMessageW(uint threadId, uint msg, nuint wParam, nint lParam);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();
    }
}
