using Remote.Adb.Desktop.Common.Converters;

namespace Remote.Adb.Desktop.Devices;

/// <summary>
/// Maps an AVD device tag (e.g. "Google TV", "Wear OS 6.0") to a Phosphor glyph from
/// <c>Themes/Icons.axaml</c>. Falls back to the Android mark for unknown/missing tags.
/// </summary>
public sealed class DeviceTagToGeometryConverter : IconConverter
{
    protected override string KeyFor(object? value) => ResolveKey(value as string);

    private static string ResolveKey(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return "IconAndroidLogo";
        }

        if (tag.Contains("TV", StringComparison.OrdinalIgnoreCase))
        {
            return "IconTelevision";
        }

        if (tag.Contains("Wear", StringComparison.OrdinalIgnoreCase))
        {
            return "IconWatch";
        }

        if (tag.Contains("Tablet", StringComparison.OrdinalIgnoreCase))
        {
            return "IconDeviceTablet";
        }

        if (tag.Contains("Phone", StringComparison.OrdinalIgnoreCase) || tag.Contains("Mobile", StringComparison.OrdinalIgnoreCase))
        {
            return "IconDeviceMobile";
        }

        return "IconAndroidLogo";
    }
}
