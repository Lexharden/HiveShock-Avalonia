using HiveShock.Logging;
using SharpHook;
using SharpHook.Data;

namespace HiveShock.Voice;

/// <summary>
/// Atajo global de mute (funciona aunque la ventana de HiveShock no tenga el foco) vía
/// SharpHook, que envuelve libuiohook y es multiplataforma de verdad (Win/Mac/Linux).
/// Combos tipo "Control+Alt+M": cada token se resuelve a uno o más <see cref="KeyCode"/>
/// (los modificadores aceptan la tecla izquierda o derecha), y se dispara una sola vez
/// por pulsación mientras la combinación se mantiene completa.
/// </summary>
public sealed class SharpHookGlobalHotkeyListener : IGlobalHotkeyListener
{
    private static readonly Dictionary<string, KeyCode[]> ModifierAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Control"] = [KeyCode.VcLeftControl, KeyCode.VcRightControl],
        ["Ctrl"] = [KeyCode.VcLeftControl, KeyCode.VcRightControl],
        ["Alt"] = [KeyCode.VcLeftAlt, KeyCode.VcRightAlt],
        ["Shift"] = [KeyCode.VcLeftShift, KeyCode.VcRightShift],
        ["Meta"] = [KeyCode.VcLeftMeta, KeyCode.VcRightMeta],
        ["Win"] = [KeyCode.VcLeftMeta, KeyCode.VcRightMeta],
        ["Super"] = [KeyCode.VcLeftMeta, KeyCode.VcRightMeta],
        ["Cmd"] = [KeyCode.VcLeftMeta, KeyCode.VcRightMeta],
    };

    private readonly HashSet<KeyCode> _pressed = [];
    private readonly object _gate = new();
    private IGlobalHook? _hook;
    private KeyCode[][] _comboGroups = [];
    private bool _triggered;

    public bool IsListening { get; private set; }

    public event Action? MuteToggleRequested;

    public void Start(string comboText)
    {
        lock (_gate)
        {
            _comboGroups = ParseCombo(comboText);
            _pressed.Clear();
            _triggered = false;

            if (IsListening)
            {
                return;
            }

            var hook = new EventLoopGlobalHook();
            hook.KeyPressed += OnKeyPressed;
            hook.KeyReleased += OnKeyReleased;
            _hook = hook;
            _ = hook.RunAsync().ContinueWith(
                t => BridgeLog.Warn($"Atajo global: {t.Exception?.GetBaseException().Message}"),
                TaskContinuationOptions.OnlyOnFaulted);
            IsListening = true;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!IsListening)
            {
                return;
            }

            _hook?.Stop();
            _hook?.Dispose();
            _hook = null;
            _pressed.Clear();
            _triggered = false;
            IsListening = false;
        }
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        lock (_gate)
        {
            _pressed.Add(e.Data.KeyCode);
            if (!_triggered && IsComboSatisfied())
            {
                _triggered = true;
                MuteToggleRequested?.Invoke();
            }
        }
    }

    private void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        lock (_gate)
        {
            _pressed.Remove(e.Data.KeyCode);
            if (_triggered && !IsComboSatisfied())
            {
                _triggered = false;
            }
        }
    }

    private bool IsComboSatisfied()
    {
        if (_comboGroups.Length == 0)
        {
            return false;
        }

        foreach (var group in _comboGroups)
        {
            var any = false;
            foreach (var code in group)
            {
                if (_pressed.Contains(code))
                {
                    any = true;
                    break;
                }
            }

            if (!any)
            {
                return false;
            }
        }

        return true;
    }

    private static KeyCode[][] ParseCombo(string comboText)
    {
        var tokens = comboText.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var groups = new List<KeyCode[]>();
        foreach (var token in tokens)
        {
            if (ModifierAliases.TryGetValue(token, out var codes))
            {
                groups.Add(codes);
                continue;
            }

            if (Enum.TryParse<KeyCode>("Vc" + token.ToUpperInvariant(), out var code))
            {
                groups.Add([code]);
            }
        }

        return groups.ToArray();
    }

    public void Dispose() => Stop();
}
