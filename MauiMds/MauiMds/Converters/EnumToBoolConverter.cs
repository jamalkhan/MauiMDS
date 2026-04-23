using System.Globalization;

namespace MauiMds.Converters;

/// <summary>
/// Converts between an enum value and bool for RadioButton IsChecked bindings.
/// ConverterParameter is the enum member name to match (e.g. "AppleSpeech").
/// </summary>
public sealed class EnumToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is not string enumName)
        {
            return false;
        }

        return value.ToString() == enumName;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not true || parameter is not string enumName)
        {
            return null;
        }

        return Enum.Parse(targetType, enumName);
    }
}
