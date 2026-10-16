namespace HiveShock.Input;

/// <summary>Tecla física: scancode set 1 y si lleva el prefijo E0 (flechas, Ctrl/Alt derechos…).</summary>
public readonly record struct KeyCode(ushort Scan, bool Extended, string Name);

/// <summary>
/// Tabla de teclas por nombre. Se usan scancodes (no virtual-keys) porque los juegos con DirectInput o RawInput
/// ignoran las virtual-keys de <c>SendInput</c>.
/// </summary>
public static class GameKeys
{
    private static readonly Dictionary<string, KeyCode> Table = Build();

    public static IReadOnlyCollection<string> Names => Table.Keys;

    public static bool TryGet(string? name, out KeyCode key)
    {
        key = default;
        return !string.IsNullOrWhiteSpace(name) && Table.TryGetValue(name.Trim(), out key);
    }

    public static bool IsModifier(string name) =>
        name.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("RCtrl", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Alt", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("RAlt", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Shift", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("RShift", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, KeyCode> Build()
    {
        var t = new Dictionary<string, KeyCode>(StringComparer.OrdinalIgnoreCase);

        void Add(string name, ushort scan, bool ext = false) => t[name] = new KeyCode(scan, ext, name);

        // Fila de letras (QWERTY, posición física).
        const string row1 = "QWERTYUIOP";
        const string row2 = "ASDFGHJKL";
        const string row3 = "ZXCVBNM";
        for (var i = 0; i < row1.Length; i++) Add(row1[i].ToString(), (ushort)(0x10 + i));
        for (var i = 0; i < row2.Length; i++) Add(row2[i].ToString(), (ushort)(0x1E + i));
        for (var i = 0; i < row3.Length; i++) Add(row3[i].ToString(), (ushort)(0x2C + i));

        for (var i = 1; i <= 9; i++) Add(i.ToString(), (ushort)(0x01 + i));
        Add("0", 0x0B);

        for (var i = 1; i <= 10; i++) Add($"F{i}", (ushort)(0x3A + i));
        Add("F11", 0x57);
        Add("F12", 0x58);

        Add("Esc", 0x01);
        Add("Tab", 0x0F);
        Add("Enter", 0x1C);
        Add("Space", 0x39);
        Add("Backspace", 0x0E);
        Add("Ctrl", 0x1D);
        Add("RCtrl", 0x1D, true);
        Add("Alt", 0x38);
        Add("RAlt", 0x38, true);
        Add("Shift", 0x2A);
        Add("RShift", 0x36);
        Add("Win", 0x5B, true);
        Add("Del", 0x53, true);
        Add("Insert", 0x52, true);
        Add("Home", 0x47, true);
        Add("End", 0x4F, true);
        Add("PageUp", 0x49, true);
        Add("PageDown", 0x51, true);
        Add("Up", 0x48, true);
        Add("Left", 0x4B, true);
        Add("Right", 0x4D, true);
        Add("Down", 0x50, true);

        Add("Minus", 0x0C);
        Add("Equals", 0x0D);
        Add("Comma", 0x33);
        Add("Period", 0x34);
        Add("Slash", 0x35);
        Add("Semicolon", 0x27);
        Add("Quote", 0x28);
        Add("LBracket", 0x1A);
        Add("RBracket", 0x1B);
        Add("Backslash", 0x2B);
        Add("Grave", 0x29);

        // Teclado numérico.
        ushort[] num = [0x52, 0x4F, 0x50, 0x51, 0x4B, 0x4C, 0x4D, 0x47, 0x48, 0x49];
        for (var i = 0; i < 10; i++) Add($"Num{i}", num[i]);

        return t;
    }
}
