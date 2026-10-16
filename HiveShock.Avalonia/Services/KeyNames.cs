using Avalonia.Input;

namespace HiveShock.Avalonia.Services;

/// <summary>Traduce la tecla que pulsa el usuario (captura en la página Teclas) al nombre de <c>GameKeys</c>.</summary>
public static class KeyNames
{
    public static string? From(Key key)
    {
        if (key is >= Key.A and <= Key.Z)
        {
            return key.ToString();
        }

        if (key is >= Key.D0 and <= Key.D9)
        {
            return ((int)key - (int)Key.D0).ToString();
        }

        if (key is >= Key.F1 and <= Key.F12)
        {
            return key.ToString();
        }

        if (key is >= Key.NumPad0 and <= Key.NumPad9)
        {
            return "Num" + ((int)key - (int)Key.NumPad0);
        }

        return key switch
        {
            Key.Space => "Space",
            Key.Enter => "Enter",
            Key.Escape => "Esc",
            Key.Tab => "Tab",
            Key.Back => "Backspace",
            Key.Delete => "Del",
            Key.Insert => "Insert",
            Key.Home => "Home",
            Key.End => "End",
            Key.PageUp => "PageUp",
            Key.PageDown => "PageDown",
            Key.Left => "Left",
            Key.Right => "Right",
            Key.Up => "Up",
            Key.Down => "Down",
            Key.LeftCtrl => "Ctrl",
            Key.RightCtrl => "RCtrl",
            Key.LeftShift => "Shift",
            Key.RightShift => "RShift",
            Key.LeftAlt => "Alt",
            Key.RightAlt => "RAlt",
            Key.LWin or Key.RWin => "Win",
            Key.OemMinus => "Minus",
            Key.OemPlus => "Equals",
            Key.OemComma => "Comma",
            Key.OemPeriod => "Period",
            Key.OemQuestion => "Slash",
            Key.OemSemicolon => "Semicolon",
            Key.OemQuotes => "Quote",
            Key.OemOpenBrackets => "LBracket",
            Key.OemCloseBrackets => "RBracket",
            Key.OemPipe => "Backslash",
            Key.OemTilde => "Grave",
            _ => null,
        };
    }
}
