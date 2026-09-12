using System.Globalization;
using Avalonia.Data.Converters;
using HiveShock.Avalonia.Services;
using HiveShock.Avalonia.ViewModels;
using HiveShock.Configuration;

namespace HiveShock.Avalonia.Converters;

public sealed class GiftImageConverter : IValueConverter
{
    public static readonly GiftImageConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            GiftRowViewModel row => row.Thumb,
            CatalogGift catalog => GiftImageLoader.Load(catalog.ImagePath),
            EditableGift gift => GiftImageLoader.Load(gift.ResolvedImagePath),
            string path => GiftImageLoader.Load(path),
            _ => null,
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
