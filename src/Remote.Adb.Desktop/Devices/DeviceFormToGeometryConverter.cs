using Remote.Adb.Core.Adb;
using Remote.Adb.Desktop.Common.Converters;

namespace Remote.Adb.Desktop.Devices;

/// <summary>
/// Maps a physical device's <see cref="DeviceForm"/> to a Phosphor glyph from <c>Themes/Icons.axaml</c>.
/// Falls back to the phone mark.
/// </summary>
public sealed class DeviceFormToGeometryConverter : IconConverter
{
    protected override string KeyFor(object? value) => value switch
    {
        DeviceForm.Tablet => "IconDeviceTablet",
        DeviceForm.Watch => "IconWatch",
        DeviceForm.Television => "IconTelevision",
        _ => "IconDeviceMobile",
    };
}
