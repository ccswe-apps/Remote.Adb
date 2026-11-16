namespace Remote.Adb.Core.Adb;

/// <summary>
/// Resolves a device's friendly <see cref="DeviceDetails"/> from its properties, cached per connection (serial and
/// adb transport id) so the device-list poll doesn't re-query every refresh, yet a reused serial (a new emulator on
/// <c>emulator-5554</c>, another phone on the same Wi-Fi address) is re-read.
/// </summary>
internal interface IDeviceDetailsResolver
{
    /// <summary>
    /// Drops cached details for every connection not in <paramref name="onlineDevices"/> — the devices just listed
    /// online — so a device that went away is re-read if its serial comes back.
    /// </summary>
    void Retain(IReadOnlyCollection<AdbDevice> onlineDevices);

    /// <summary>Returns <paramref name="device"/>'s details, or <see langword="null"/> if they can't be read.</summary>
    Task<DeviceDetails?> ResolveAsync(AdbDevice device, CancellationToken cancellationToken = default);
}
