using HiveShock.Logging;
using SharpHook;
using SharpHook.Data;

namespace HiveShock.Voice;

/// <summary>
/// Atajos globales (funcionan aunque la ventana de HiveShock no tenga el foco) vía
/// SharpHook, que envuelve libuiohook y es multiplataforma de verdad (Win/Mac/Linux).
/// Combos tipo "Control+Alt+M": cada token se resuelve a uno o más <see cref="KeyCode"/>
/// (los modificadores aceptan la tecla izquierda o derecha), y cada combo se dispara una
/// sola vez por pulsación mientras se mantiene completo. Un solo hook sirve a todos.
/// </summary>
public sealed class SharpHookGlobalHotkeyListener : IGlobalHotkeyListener
{
    private static readonly Dictionary<string, KeyCode[]> ModifierAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Control"] = [KeyCode.VcLeftControl, KeyCode.VcRightControl],
        ["Ctrl"] = [KeyCode.VcLeftControl, KeyCode.VcRightControl],
        ["Alt"] = [KeyCode.VcLeftAlt, KeyCode.VcRightAlt],
        ["Shift"] = [KeyCode.VcLeftShift, KeyCode.VcRightShift],
        ["Mayús"] = [KeyCode.VcLeftShift, KeyCode.VcRightShift],
        ["Meta"] = [KeyCode.VcLeftMeta, KeyCode.VcRightMeta],
        ["Win"] = [KeyCode.VcLeftMeta, KeyCode.VcRightMeta],
        ["Super"] = [KeyCode.VcLeftMeta, KeyCode.VcRightMeta],
        ["Cmd"] = [KeyCode.VcLeftMeta, KeyCode.VcRightMeta],
    };

    private readonly HashSet<KeyCode> _pressed = [];
    private readonly object _gate = new();
    private IGlobalHook? _hook;
    private Binding[] _bindings = [];

    public bool IsListening { get; private set; }

    public event Action? MuteToggleRequested;
    public event Action? SkipRequested;

    public bool IsValidCombo(string comboText) => ParseCombo(comboText) != null;

    public void Start(string muteCombo, string skipCombo)
    {
        lock (_gate)
        {
            var bindings = new List<Binding>();
            if (ParseCombo(muteCombo) is { } mute)
            {
                bindings.Add(new Binding(mute, () => MuteToggleRequested?.Invoke()));
            }

            if (ParseCombo(skipCombo) is { } skip)
            {
                bindings.Add(new Binding(skip, () => SkipRequested?.Invoke()));
            }

            _bindings = bindings.ToArray();
            _pressed.Clear();

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

            try
            {
                // libuiohook es global al proceso: si el hook ya murió (o otro lo detuvo),
                // Stop lanza HookException. Apagar la voz nunca debe tumbar la app por eso.
                _hook?.Stop();
                _hook?.Dispose();
            }
            catch (Exception ex)
            {
                BridgeLog.Warn($"Atajo global al detener: {ex.Message}");
            }

            _hook = null;
            _pressed.Clear();
            _bindings = [];
            IsListening = false;
        }
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        List<Action>? fire = null;
        lock (_gate)
        {
            _pressed.Add(e.Data.KeyCode);
            foreach (var binding in _bindings)
            {
                if (!binding.Triggered && IsComboSatisfied(binding.Groups))
                {
                    binding.Triggered = true;
                    (fire ??= []).Add(binding.Action);
                }
            }
        }

        // Fuera del lock: la acción puede tardar (cortar audio) y no debe frenar el hook.
        fire?.ForEach(a => a());
    }

    private void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        lock (_gate)
        {
            _pressed.Remove(e.Data.KeyCode);
            foreach (var binding in _bindings)
            {
                if (binding.Triggered && !IsComboSatisfied(binding.Groups))
                {
                    binding.Triggered = false;
                }
            }
        }
    }

    private bool IsComboSatisfied(KeyCode[][] groups)
    {
        foreach (var group in groups)
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

    /// <summary>Null si el texto está vacío, tiene una tecla desconocida o no incluye ninguna tecla normal.</summary>
    private static KeyCode[][]? ParseCombo(string? comboText)
    {
        var tokens = (comboText ?? "").Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return null;
        }

        var groups = new List<KeyCode[]>();
        var hasMainKey = false;
        foreach (var token in tokens)
        {
            if (ModifierAliases.TryGetValue(token, out var codes))
            {
                groups.Add(codes);
                continue;
            }

            if (Enum.TryParse<KeyCode>("Vc" + token.ToUpperInvariant(), out var code) ||
                Enum.TryParse(("Vc" + token), true, out code))
            {
                groups.Add([code]);
                hasMainKey = true;
                continue;
            }

            return null;
        }

        return hasMainKey ? groups.ToArray() : null;
    }

    public void Dispose() => Stop();

    private sealed class Binding(KeyCode[][] groups, Action action)
    {
        public KeyCode[][] Groups { get; } = groups;
        public Action Action { get; } = action;
        public bool Triggered { get; set; }
    }
}
