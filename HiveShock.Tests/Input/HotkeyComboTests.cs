using HiveShock.Input;

namespace HiveShock.Tests.Input;

public class HotkeyComboTests
{
    [Fact]
    public void Parsea_modificadores_y_tecla()
    {
        Assert.True(HotkeyCombo.TryParse("ctrl+alt+f5", out var combo));

        Assert.Equal(HotkeyCombo.Ctrl | HotkeyCombo.Alt, combo.Modifiers);
        Assert.Equal(0x74u, combo.VirtualKey);
        Assert.Equal("Ctrl+Alt+F5", combo.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("F5")]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+Banana")]
    [InlineData("Pepe+F5")]
    [InlineData("Ctrl+Shift")]
    public void Rechaza_atajos_sin_modificador_o_con_tecla_desconocida(string text) =>
        Assert.False(HotkeyCombo.TryParse(text, out _));

    [Fact]
    public void Format_ordena_los_modificadores_igual_que_el_parser()
    {
        var text = HotkeyCombo.Format(HotkeyCombo.Shift | HotkeyCombo.Ctrl, "k");

        Assert.Equal("Ctrl+Shift+K", text);
        Assert.True(HotkeyCombo.TryParse(text, out var combo));
        Assert.Equal(text, combo.Text);
    }

    [Fact]
    public void El_validador_rechaza_un_atajo_invalido()
    {
        var macro = new KeyMacro
        {
            Id = "m",
            Hotkey = "F5",
            Steps = [new KeyStep { Key = "Space" }],
        };

        Assert.NotEmpty(KeyMacroValidator.Validate(macro));
        Assert.Empty(KeyMacroValidator.Validate(macro with { Hotkey = "Ctrl+F5" }));
    }

    [Fact]
    public void Sin_soporte_cada_atajo_se_reporta_como_fallo()
    {
        using var hotkeys = new NullGlobalHotkeys();
        HotkeyCombo.TryParse("Ctrl+F5", out var combo);

        var failures = hotkeys.Apply([new HotkeyBinding("m", combo, () => { })]);

        Assert.Equal("m", Assert.Single(failures).Id);
    }
}
