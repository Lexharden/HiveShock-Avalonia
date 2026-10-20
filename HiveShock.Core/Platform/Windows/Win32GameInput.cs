using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using HiveShock.Input;

namespace HiveShock.Platform.Windows;

public sealed record WindowInfo(nint Handle, string Title, string ProcessName, int ProcessId);

/// <summary>
/// Teclas hacia la ventana del juego con <c>SendInput</c> (scancodes). Trae el juego al frente, y al terminar
/// devuelve el foco a la ventana que tenía el usuario.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class Win32GameInput : IGameInput
{
    private const int FocusAttempts = 3;
    private const int SwRestore = 9;

    // Un solo robo de foco a la vez: dos macros no deben pelearse por la ventana.
    private readonly SemaphoreSlim _focusLock = new(1, 1);

    public WindowStatus GetStatus(WindowTarget target)
    {
        var win = Find(target);
        if (win is null)
        {
            return WindowStatus.NotFound;
        }

        if (IsElevated(win.ProcessId) is not false && !IsElevated(Environment.ProcessId).GetValueOrDefault())
        {
            return WindowStatus.Elevated;
        }

        return NativeMethods.IsIconic(win.Handle) ? WindowStatus.Minimized : WindowStatus.Ready;
    }

    public async Task<IInputSession?> BeginAsync(WindowTarget target, CancellationToken ct)
    {
        await _focusLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var win = Find(target);
            if (win is null)
            {
                _focusLock.Release();
                return null;
            }

            var previous = NativeMethods.GetForegroundWindow();
            if (!await FocusAsync(win.Handle, ct).ConfigureAwait(false))
            {
                _focusLock.Release();
                return null;
            }

            return new Session(this, win.Handle, previous);
        }
        catch
        {
            _focusLock.Release();
            throw;
        }
    }

    /// <summary>Ventanas visibles de primer nivel, para el selector de la UI.</summary>
    public static IReadOnlyList<WindowInfo> ListWindows() => Enumerate().ToList();

    public static WindowInfo? Find(WindowTarget target)
    {
        if (target.IsEmpty)
        {
            return null;
        }

        Regex? regex = null;
        if (!string.IsNullOrWhiteSpace(target.TitleRegex))
        {
            try
            {
                regex = new Regex(target.TitleRegex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        var proc = target.ProcessName?.Trim();
        if (proc != null && proc.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            proc = proc[..^4];
        }

        foreach (var w in Enumerate())
        {
            if (!string.IsNullOrEmpty(proc) && !w.ProcessName.Equals(proc, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(target.TitleContains) &&
                !w.Title.Contains(target.TitleContains, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                if (regex != null && !regex.IsMatch(w.Title))
                {
                    continue;
                }
            }
            catch (RegexMatchTimeoutException)
            {
                continue;
            }

            return w;
        }

        return null;
    }

    private static IEnumerable<WindowInfo> Enumerate()
    {
        var found = new List<nint>();
        NativeMethods.EnumWindows((h, _) =>
        {
            if (NativeMethods.IsWindowVisible(h) &&
                NativeMethods.GetWindow(h, 4 /* GW_OWNER */) == 0 &&
                NativeMethods.GetWindowTextLengthW(h) > 0)
            {
                found.Add(h);
            }

            return true;
        }, 0);

        var names = new Dictionary<uint, string>();
        foreach (var h in found)
        {
            NativeMethods.GetWindowThreadProcessId(h, out var pid);
            if (!names.TryGetValue(pid, out var name))
            {
                try
                {
                    using var p = Process.GetProcessById((int)pid);
                    name = p.ProcessName;
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
                {
                    name = "";
                }

                names[pid] = name;
            }

            var sb = new StringBuilder(NativeMethods.GetWindowTextLengthW(h) + 1);
            NativeMethods.GetWindowTextW(h, sb, sb.Capacity);
            yield return new WindowInfo(h, sb.ToString(), name, (int)pid);
        }
    }

    /// <summary>true/false si se pudo saber; null si no (sin permisos).</summary>
    public static bool? IsElevated(int processId)
    {
        var process = NativeMethods.OpenProcess(0x1000 /* QUERY_LIMITED_INFORMATION */, false, (uint)processId);
        if (process == 0)
        {
            // Si ni siquiera se puede abrir, es de otro nivel de integridad: se trata como elevado.
            return null;
        }

        try
        {
            if (!NativeMethods.OpenProcessToken(process, 0x8 /* TOKEN_QUERY */, out var token))
            {
                return null;
            }

            try
            {
                return NativeMethods.GetTokenInformation(token, 20 /* TokenElevation */, out var elevation, sizeof(int), out _)
                    ? elevation != 0
                    : null;
            }
            finally
            {
                NativeMethods.CloseHandle(token);
            }
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }

    private static async Task<bool> FocusAsync(nint hwnd, CancellationToken ct)
    {
        for (var attempt = 0; attempt < FocusAttempts; attempt++)
        {
            if (NativeMethods.IsIconic(hwnd))
            {
                NativeMethods.ShowWindow(hwnd, SwRestore);
            }

            ForceForeground(hwnd);
            await Task.Delay(80, ct).ConfigureAwait(false);
            if (NativeMethods.GetForegroundWindow() == hwnd)
            {
                // El juego necesita unos fotogramas para aceptar entrada tras recuperar el foco.
                await Task.Delay(60, ct).ConfigureAwait(false);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Windows impide que una app robe el foco. Adjuntarse a la cola de entrada del hilo en primer plano
    /// lo permite de forma fiable.
    /// </summary>
    private static void ForceForeground(nint hwnd)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        var fgThread = foreground == 0 ? 0 : NativeMethods.GetWindowThreadProcessId(foreground, out _);
        var current = NativeMethods.GetCurrentThreadId();
        var attached = fgThread != 0 && fgThread != current && NativeMethods.AttachThreadInput(current, fgThread, true);
        try
        {
            NativeMethods.BringWindowToTop(hwnd);
            NativeMethods.SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached)
            {
                NativeMethods.AttachThreadInput(current, fgThread, false);
            }
        }
    }

    private sealed class Session(Win32GameInput owner, nint target, nint previous) : IInputSession
    {
        private int _disposed;

        public void Send(KeyCode key, bool down)
        {
            var flags = 0x0008u; // KEYEVENTF_SCANCODE
            if (!down)
            {
                flags |= 0x0002; // KEYUP
            }

            if (key.Extended)
            {
                flags |= 0x0001; // EXTENDEDKEY
            }

            var input = new NativeMethods.INPUT
            {
                type = 1,
                u = new NativeMethods.InputUnion
                {
                    ki = new NativeMethods.KEYBDINPUT { wScan = key.Scan, dwFlags = flags },
                },
            };

            if (NativeMethods.SendInput(1, [input], Marshal.SizeOf<NativeMethods.INPUT>()) == 0)
            {
                throw new InvalidOperationException(
                    $"Windows rechazó la tecla {key.Name} (¿el juego corre como administrador?): {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                if (previous != 0 && previous != target && NativeMethods.IsWindow(previous))
                {
                    ForceForeground(previous);
                }
            }
            finally
            {
                owner._focusLock.Release();
            }
        }
    }

    private static class NativeMethods
    {
        public delegate bool EnumWindowsProc(nint hwnd, nint lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public nint dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public nint dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsProc proc, nint lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(nint hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindow(nint hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(nint hwnd);

        [DllImport("user32.dll")]
        public static extern nint GetWindow(nint hwnd, uint cmd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextLengthW(nint hwnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextW(nint hwnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

        [DllImport("user32.dll")]
        public static extern nint GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(nint hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BringWindowToTop(nint hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(nint hwnd, int cmd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool attach);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool OpenProcessToken(nint process, uint access, out nint token);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetTokenInformation(nint token, int infoClass, out int info, int length, out int returned);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(nint handle);
    }
}
