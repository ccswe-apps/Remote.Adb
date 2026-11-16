using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Remote.Adb.Desktop.Common.Converters;

/// <summary>
/// Base for the converters that pick a glyph: derived converters map the bound value to an icon's resource key, and
/// this resolves the key to the <see cref="Geometry"/> registered under it in the application resources
/// (<c>Themes/Icons.axaml</c>). One-way only.
/// </summary>
public abstract class IconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (KeyFor(value) is { } key
            && Application.Current is { } application
            && application.TryGetResource(key, null, out var resource)
            && resource is Geometry geometry)
        {
            return geometry;
        }

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    /// <summary>The resource key of the icon for <paramref name="value"/>, or <see langword="null"/> for none.</summary>
    protected abstract string? KeyFor(object? value);
}
