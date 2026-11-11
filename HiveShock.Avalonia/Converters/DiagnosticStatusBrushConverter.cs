using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace HiveShock.Avalonia.Converters;

/// <summary>
/// Color del punto de un evento de la página Debug según su <c>Status</c>
/// (gift, chat, like, follow, share, system, unknown). Usa los tokens del tema para verse bien en claro y oscuro.
/// </summary>
public sealed class DiagnosticStatusBrushConverter : IValueConverter
{
    public static readonly DiagnosticStatusBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = (value as string)?.ToLowerInvariant() switch
        {
            "gift" => "BrandGoldBrush",
            "chat" => "AccentTextBrush",
            "like" => "DangerBrush",
            "follow" => "StatusOkBrush",
            "share" => "LiveTextBrush",
            "system" => "MutedBrush",
            _ => "FaintBrush",
        };

        return Application.Current?.TryGetResource(key, Application.Current.ActualThemeVariant, out var brush) == true
            ? brush as IBrush
            : Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
