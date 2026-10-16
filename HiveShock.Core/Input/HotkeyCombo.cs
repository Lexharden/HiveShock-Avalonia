namespace HiveShock.Input;

/// <summary>Combinación global tipo «Ctrl+Alt+F5»: modificadores de Windows (RegisterHotKey) + tecla virtual.</summary>
public readonly record struct HotkeyCombo(uint Modifiers, uint VirtualKey, string Text)
{
    public const uint Alt = 0x1;
    public const uint Ctrl = 0x2;
    public const uint Shift = 0x4;
    public const uint Win = 0x8;

    private static readonly Dictionary<string, uint> Keys = BuildKeys();

    /// <summary>Exige al menos un modificador: un atajo global sin él se tragaría esa tecla en todo el sistema (y en el juego).</summary>
    public static bool TryParse(string? text, out HotkeyCombo combo)
    {
        combo = default;
        var tokens = (text ?? "").Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length < 2)
        {
            return false;
        }

        uint mods = 0;
        for (var i = 0; i < tokens.Length - 1; i++)
        {
            switch (tokens[i].ToLowerInvariant())
            {
                case "ctrl" or "control":
                    mods |= Ctrl;
                    break;
                case "alt":
                    mods |= Alt;
                    break;
                case "shift" or "mayús" or "mayus":
                    mods |= Shift;
                    break;
                case "win" or "meta" or "super":
                    mods |= Win;
                    break;
                default:
                    return false;
            }
        }

        if (!Keys.TryGetValue(tokens[^1], out var vk))
        {
            return false;
        }

        combo = new HotkeyCombo(mods, vk, Format(mods, tokens[^1]));
        return true;
    }

    /// <summary>Texto canónico («Ctrl+Alt+Shift+Win+Tecla»), el mismo que guarda la macro.</summary>
    public static string Format(uint modifiers, string keyName)
    {
        var parts = new List<string>(5);
        if ((modifiers & Ctrl) != 0)
        {
            parts.Add("Ctrl");
        }

        if ((modifiers & Alt) != 0)
        {
            parts.Add("Alt");
        }

        if ((modifiers & Shift) != 0)
        {
            parts.Add("Shift");
        }

        if ((modifiers & Win) != 0)
        {
            parts.Add("Win");
        }

        parts.Add(Keys.Keys.FirstOrDefault(k => k.Equals(keyName, StringComparison.OrdinalIgnoreCase)) ?? keyName);
        return string.Join('+', parts);
    }

    private static Dictionary<string, uint> BuildKeys()
    {
        var t = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        for (var c = 'A'; c <= 'Z'; c++)
        {
            t[c.ToString()] = c;
        }

        for (var c = '0'; c <= '9'; c++)
        {
            t[c.ToString()] = c;
        }

        for (var i = 1; i <= 12; i++)
        {
            t[$"F{i}"] = (uint)(0x6F + i);
        }

        for (var i = 0; i <= 9; i++)
        {
            t[$"Num{i}"] = (uint)(0x60 + i);
        }

        t["Space"] = 0x20;
        t["Enter"] = 0x0D;
        t["Esc"] = 0x1B;
        t["Tab"] = 0x09;
        t["Backspace"] = 0x08;
        t["Del"] = 0x2E;
        t["Insert"] = 0x2D;
        t["Home"] = 0x24;
        t["End"] = 0x23;
        t["PageUp"] = 0x21;
        t["PageDown"] = 0x22;
        t["Left"] = 0x25;
        t["Up"] = 0x26;
        t["Right"] = 0x27;
        t["Down"] = 0x28;
        t["Minus"] = 0xBD;
        t["Equals"] = 0xBB;
        t["Comma"] = 0xBC;
        t["Period"] = 0xBE;
        t["Slash"] = 0xBF;
        t["Semicolon"] = 0xBA;
        t["Quote"] = 0xDE;
        t["LBracket"] = 0xDB;
        t["RBracket"] = 0xDD;
        t["Backslash"] = 0xDC;
        t["Grave"] = 0xC0;
        return t;
    }
}

public sealed record HotkeyBinding(string Id, HotkeyCombo Combo, Action OnPressed);

public sealed record HotkeyFailure(string Id, string Reason);

/// <summary>Atajos globales (valen aunque HiveShock no tenga el foco).</summary>
public interface IGlobalHotkeys : IDisposable
{
    /// <summary>Sustituye todos los atajos registrados por estos. Devuelve los que no se pudieron registrar.</summary>
    IReadOnlyList<HotkeyFailure> Apply(IReadOnlyList<HotkeyBinding> bindings);
}

public sealed class NullGlobalHotkeys : IGlobalHotkeys
{
    public IReadOnlyList<HotkeyFailure> Apply(IReadOnlyList<HotkeyBinding> bindings) =>
        [.. bindings.Select(b => new HotkeyFailure(b.Id, "Los atajos globales solo funcionan en Windows."))];

    public void Dispose()
    {
    }
}
