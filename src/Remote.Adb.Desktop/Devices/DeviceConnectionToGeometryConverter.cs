using Remote.Adb.Core.Adb;
using Remote.Adb.Desktop.Common.Converters;

namespace Remote.Adb.Desktop.Devices;

/// <summary>
/// Maps a physical device's <see cref="DeviceConnection"/> to a Phosphor glyph from <c>Themes/Icons.axaml</c>:
/// a USB mark for a wired device, a wifi mark for a wireless one.
/// </summary>
public sealed class DeviceConnectionToGeometryConverter : IconConverter
{
    protected override string KeyFor(object? value) => value is DeviceConnection.Wireless ? "IconWifiHigh" : "IconUsb";
}
