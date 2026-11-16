namespace Remote.Adb.Desktop.Common.Converters;

/// <summary>
/// Resolves a resource key (e.g. <c>"IconAndroidLogo"</c>) to the <see cref="Avalonia.Media.Geometry"/>
/// registered under that key in the application resources (<c>Themes/Icons.axaml</c>).
/// </summary>
public sealed class ResourceKeyToGeometryConverter : IconConverter
{
    protected override string? KeyFor(object? value) => value as string;
}
