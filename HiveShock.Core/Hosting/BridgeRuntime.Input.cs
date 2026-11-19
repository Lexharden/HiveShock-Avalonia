using HiveShock.Configuration;
using HiveShock.Input;
using HiveShock.Logging;

namespace HiveShock.Hosting;

/// <summary>Macros de teclas y atajos globales (la biblioteca es común a todos los perfiles).</summary>
public sealed partial class BridgeRuntime
{
    /// <summary>Macros de la biblioteca global (no dependen del juego elegido).</summary>
    public IReadOnlyList<KeyMacro> LoadKeyMacros() =>
        [.. _library.Macros.OrderBy(m => m.Label, StringComparer.OrdinalIgnoreCase)];

    /// <summary>Ventana a la que se envían las teclas (global, no por perfil).</summary>
    public WindowTarget? GameWindow => _library.Window;

    /// <summary>
    /// Guarda la biblioteca (macros + ventana) y deja las macros activas al instante, también con el puente en
    /// marcha. Si no se puede escribir en disco lanza y NO cambia lo que está activo.
    /// </summary>
    public void SaveMacroLibrary(IReadOnlyCollection<KeyMacro> macros, WindowTarget? window)
    {
        var library = new MacroLibrary
        {
            Window = window is { IsEmpty: false } ? window : null,
            Macros = [.. macros],
        };
        MacroLibraryStore.Save(library);
        _library = library;
        _effects.SetMacros(new KeyMacroCatalog(library.Macros));
        ApplyHotkeys();
    }

    /// <summary>Registra los atajos globales de las macros del perfil activo (sustituye los anteriores).</summary>
    public void ApplyHotkeys()
    {
        var problems = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var bindings = new List<HotkeyBinding>();
        foreach (var macro in _effects.Macros.Where(m => !string.IsNullOrWhiteSpace(m.Hotkey)))
        {
            if (!HotkeyCombo.TryParse(macro.Hotkey, out var combo))
            {
                problems[macro.Id] = $"Atajo «{macro.Hotkey}» no válido.";
                continue;
            }

            var id = macro.Id;
            bindings.Add(new HotkeyBinding(id, combo, () => TryRunMacro(id, "Atajo", "atajo")));
        }

        _cancelHotkeyOn = SystemEffects.IsCountingDown;
        if (_cancelHotkeyOn && HotkeyCombo.TryParse(SystemEffectService.CancelHotkey, out var cancelCombo))
        {
            bindings.Add(new HotkeyBinding("__cancelar_reinicio", cancelCombo, () => SystemEffects.Cancel("atajo de cancelar")));
        }

        foreach (var failure in _hotkeys.Apply(bindings))
        {
            problems[failure.Id] = failure.Reason;
        }

        foreach (var (id, reason) in problems)
        {
            BridgeLog.Warn($"Atajo de «{id}»: {reason}");
        }

        HotkeyProblems = problems;
    }


    public WindowStatus GetGameWindowStatus(WindowTarget window) =>
        window.IsEmpty ? WindowStatus.NotFound : _keyInput.GetStatus(window);

    /// <summary>Ventanas abiertas para elegir el juego. Vacío fuera de Windows.</summary>
    public IReadOnlyList<Platform.Windows.WindowInfo> ListGameWindows() =>
        OperatingSystem.IsWindows() ? Platform.Windows.Win32GameInput.ListWindows() : [];

    /// <summary>Prueba una macro (aunque no esté guardada). Devuelve los errores de validación; vacío = en marcha.</summary>
    public IReadOnlyList<string> TestKeyMacro(KeyMacro macro)
    {
        var errors = KeyMacroValidator.Validate(macro);
        if (errors.Count == 0)
        {
            EnsureKeyPump();
            KeyRunner.Enqueue(macro, "Prueba", "prueba");
        }

        return errors;
    }

    /// <summary>Arranca (una sola vez) el bucle que ejecuta las macros de teclas.</summary>
    private void EnsureKeyPump()
    {
        lock (_gate)
        {
            _keyPump ??= Task.Run(() => KeyRunner.RunAsync(_keyCts.Token));
        }
    }

    /// <summary>Efecto del sistema (reiniciar…) lanzado desde las pruebas de la UI. True si lo era.</summary>
    private bool TryRunSystemEffect(string effectId, string user, string reason)
    {
        if (!SystemEffectIds.IsSystemEffect(effectId) || !_effects.Contains(effectId))
        {
            return false;
        }

        SystemEffects.Trigger(effectId, user, reason);
        return true;
    }

    private bool TryRunMacro(string effectId, string user, string reason)
    {
        if (!_effects.TryGetMacro(effectId, out var macro))
        {
            return false;
        }

        EnsureKeyPump();
        KeyRunner.Enqueue(macro, user, reason);
        return true;
    }
}
