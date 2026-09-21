using System.Globalization;
using Avalonia.Data.Converters;
using HiveShock.Configuration;

namespace HiveShock.Avalonia.Converters;

/// <summary>Etiqueta en español para el tipo de una propiedad de efecto en el ComboBox.</summary>
public sealed class EditableValueKindLabelConverter : IValueConverter
{
    public static readonly EditableValueKindLabelConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            EditableValueKind.Number => "Número",
            EditableValueKind.Bool => "Sí / No",
            _ => "Texto",
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            "Número" => EditableValueKind.Number,
            "Sí / No" => EditableValueKind.Bool,
            _ => EditableValueKind.Text,
        };
}
