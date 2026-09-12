using Avalonia.Media;
using HiveShock.Avalonia.Themes;

namespace HiveShock.Avalonia.Services;

public static class OverlayLook
{
    public const string Gold = "#EBA00A";
    public const string Blue = "#075BAA";
    public const string White = "#F7F7F7";
    public const string Black = "#0E1520";

    public static Color Parse(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return fallback;
        }

        return Color.TryParse(hex.Trim(), out var color) ? color : fallback;
    }

    public static Color Number(UiPreferences prefs) =>
        Parse(prefs.OverlayNumberColor, ThemeManager.OverlayText(ThemeManager.Current));

    public static Color Title(UiPreferences prefs) =>
        Parse(prefs.OverlayTitleColor, ThemeManager.OverlayMuted(ThemeManager.Current));

    public static Color GiftText(UiPreferences prefs) =>
        Parse(prefs.OverlayGiftColor, ThemeManager.OverlayText(ThemeManager.Current));

    public static Color Card(UiPreferences prefs) =>
        Parse(prefs.OverlayBackgroundColor, ThemeManager.OverlayCard(ThemeManager.Current));

    public static Color Border(UiPreferences prefs) =>
        Parse(prefs.OverlayBackgroundColor, ThemeManager.OverlayBorder(ThemeManager.Current));

    public static double NumberSize(UiPreferences prefs) =>
        Clamp(prefs.OverlayNumberSize, 28, 140, 72);

    public static double TitleSize(UiPreferences prefs) =>
        Clamp(prefs.OverlayTitleSize, 10, 36, 16);

    public static double GiftNameSize(UiPreferences prefs) =>
        Clamp(prefs.OverlayGiftNameSize, 10, 32, 14);

    public static double BackgroundOpacity(UiPreferences prefs) =>
        Math.Clamp(prefs.OverlayBackgroundOpacity <= 0 ? 0.94 : prefs.OverlayBackgroundOpacity, 0.15, 1.0);

    public static bool AlignCenter(UiPreferences prefs) =>
        !string.Equals(prefs.OverlayAlign, "left", StringComparison.OrdinalIgnoreCase);

    public static string GiftsHeading(UiPreferences prefs)
    {
        var title = prefs.GiftsOverlayTitle?.Trim();
        return string.IsNullOrWhiteSpace(title) ? "Regalos" : title;
    }

    private static double Clamp(double value, double min, double max, double fallback)
    {
        if (double.IsNaN(value) || value <= 0)
        {
            return fallback;
        }

        return Math.Clamp(value, min, max);
    }
}
