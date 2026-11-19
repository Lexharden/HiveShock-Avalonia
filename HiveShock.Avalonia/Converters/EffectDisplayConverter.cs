using System.Globalization;
using Avalonia.Data.Converters;
using HiveShock.Avalonia.ViewModels;

namespace HiveShock.Avalonia.Converters;

/// <summary>
/// Nombre legible de un efecto para las filas de lista (maestro/detalle): [id del efecto, lista de opciones].
/// Sin efecto devuelve «Sin efecto»; si el id ya no existe en el perfil, devuelve el id tal cual.
/// </summary>
public sealed class EffectDisplayConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var id = values.Count > 0 ? values[0] as string : null;
        if (string.IsNullOrEmpty(id))
        {
            return "Sin efecto";
        }

        if (values.Count > 1 && values[1] is IEnumerable<EffectChoice> choices)
        {
            var match = choices.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.Ordinal));
            if (match != null)
            {
                return match.Display;
            }
        }

        return id;
    }
}
