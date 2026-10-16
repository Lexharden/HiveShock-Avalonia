namespace HiveShock.Input;

public static class KeyMacroValidator
{
    public const int MaxRepeat = 200;
    public const int MaxHoldMs = 30_000;
    public const int TapMs = 50;

    /// <summary>Lista vacía = válida.</summary>
    public static IReadOnlyList<string> Validate(KeyMacro macro)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(macro.Id))
        {
            errors.Add("La macro no tiene id.");
        }

        if (macro.Steps.Count == 0)
        {
            errors.Add("La macro no tiene pasos.");
        }

        var ctrl = false;
        var alt = false;
        for (var i = 0; i < macro.Steps.Count; i++)
        {
            var s = macro.Steps[i];
            var n = i + 1;
            if (!GameKeys.TryGet(s.Key, out _))
            {
                errors.Add($"Paso {n}: tecla desconocida «{s.Key}».");
                continue;
            }

            if (s.Repeat is < 1 or > MaxRepeat)
            {
                errors.Add($"Paso {n}: repetir debe estar entre 1 y {MaxRepeat}.");
            }

            if (s.Action == KeyAction.Hold && s.HoldMs is < 1 or > MaxHoldMs)
            {
                errors.Add($"Paso {n}: la duración debe estar entre 1 y {MaxHoldMs} ms.");
            }

            if (s.DelayAfterMs < 0 || s.JitterPercent is < 0 or > 100)
            {
                errors.Add($"Paso {n}: espera o variación fuera de rango.");
            }

            var key = s.Key.Trim();
            if (s.Action != KeyAction.Up)
            {
                if (key.StartsWith("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                    key.StartsWith("RCtrl", StringComparison.OrdinalIgnoreCase))
                {
                    ctrl = true;
                }

                if (key.Equals("Alt", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("RAlt", StringComparison.OrdinalIgnoreCase))
                {
                    alt = true;
                }
            }
            else
            {
                if (key.EndsWith("Ctrl", StringComparison.OrdinalIgnoreCase))
                {
                    ctrl = false;
                }

                if (key.EndsWith("Alt", StringComparison.OrdinalIgnoreCase))
                {
                    alt = false;
                }
            }

            if (!macro.AllowDangerous && IsDangerous(key, ctrl, alt))
            {
                errors.Add($"Paso {n}: «{key}» es una combinación bloqueada (Win, Alt+F4, Alt+Tab, Ctrl+Alt+Supr). " +
                           "Actívala en la macro si de verdad la quieres.");
            }
        }

        if (!string.IsNullOrWhiteSpace(macro.Hotkey) && !HotkeyCombo.TryParse(macro.Hotkey, out _))
        {
            errors.Add($"Atajo «{macro.Hotkey}» no válido: usa modificador + tecla (Ctrl+Alt+F5).");
        }

        var total = EstimateTotalMs(macro);
        if (total > macro.MaxTotalMs)
        {
            errors.Add($"Duración estimada {total} ms supera el tope de {macro.MaxTotalMs} ms.");
        }

        return errors;
    }

    public static long EstimateTotalMs(KeyMacro macro)
    {
        long total = 0;
        foreach (var s in macro.Steps)
        {
            var hold = s.Action switch
            {
                KeyAction.Tap => TapMs,
                KeyAction.Hold => Math.Max(0, s.HoldMs),
                _ => 0,
            };
            total += (hold + Math.Max(0, s.DelayAfterMs)) * (long)Math.Clamp(s.Repeat, 1, MaxRepeat);
        }

        return total;
    }

    private static bool IsDangerous(string key, bool ctrl, bool alt) =>
        key.Equals("Win", StringComparison.OrdinalIgnoreCase) ||
        (alt && (key.Equals("F4", StringComparison.OrdinalIgnoreCase) ||
                 key.Equals("Tab", StringComparison.OrdinalIgnoreCase) ||
                 key.Equals("Esc", StringComparison.OrdinalIgnoreCase))) ||
        (ctrl && alt && key.Equals("Del", StringComparison.OrdinalIgnoreCase)) ||
        (ctrl && key.Equals("Esc", StringComparison.OrdinalIgnoreCase));
}
