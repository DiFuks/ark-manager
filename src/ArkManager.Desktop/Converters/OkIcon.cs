using System.Globalization;
using Avalonia.Data.Converters;

namespace ArkManager.App.Converters;

public sealed class OkIcon : IValueConverter
{
    public static readonly OkIcon Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "✅" : "❌";
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
