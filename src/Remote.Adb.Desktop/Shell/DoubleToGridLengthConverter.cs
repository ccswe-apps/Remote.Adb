using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Remote.Adb.Desktop.Shell;

/// <summary>
/// Converts a pixel size to a pixel <see cref="GridLength"/> and back, so a row's height can bind to a plain
/// <see cref="double"/> on the view model (keeping it free of Avalonia types) while a <c>GridSplitter</c> resizes it.
/// </summary>
public sealed class DoubleToGridLengthConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double pixels ? new GridLength(pixels, GridUnitType.Pixel) : GridLength.Auto;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is GridLength { IsAbsolute: true } length ? length.Value : Avalonia.Data.BindingOperations.DoNothing;
}
