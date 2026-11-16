using System.Collections.Concurrent;
using Remote.Adb.Core.Common;

namespace Remote.Adb.Core.Adb;

/// <inheritdoc />
internal sealed class DeviceDetailsResolver : IDeviceDetailsResolver
{
    // One shell round-trip reads every prop as `key=value` lines (values may contain spaces, never newlines).
    private static readonly string GetpropScript = string.Join(
        "; ",
        new[]
        {
            "ro.kernel.qemu",
            "ro.boot.qemu.avd_name",
            "ro.build.characteristics",
            "ro.product.model",
            "ro.product.marketing.name",
            "ro.product.vendor.marketname",
            "ro.build.version.sdk",
            "ro.product.cpu.abi",
        }.Select(property => $"echo {property}=$(getprop {property})"));

    private readonly IAdbRunner _adb;
    private readonly ConcurrentDictionary<(string Serial, string? TransportId), DeviceDetails> _cache = new();

    public DeviceDetailsResolver(IAdbRunner adb)
    {
        _adb = adb;
    }

    private static (string Serial, string? TransportId) KeyOf(AdbDevice device) => (device.Serial, device.TransportId);

    /// <inheritdoc />
    public async Task<DeviceDetails?> ResolveAsync(AdbDevice device, CancellationToken cancellationToken = default)
    {
        var key = KeyOf(device);
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        ProcessResult result;
        try
        {
            result = await _adb.RunAsync(["-s", device.Serial, "shell", GetpropScript], cancellationToken: cancellationToken);
        }
        catch (ProcessLaunchException)
        {
            return null;
        }

        if (!result.Success)
        {
            return null;
        }

        var details = DeviceDetailsParser.Build(device.Serial, result.StandardOutput);
        _cache[key] = details;
        return details;
    }

    /// <inheritdoc />
    public void Retain(IReadOnlyCollection<AdbDevice> onlineDevices)
    {
        var listed = onlineDevices.Select(KeyOf).ToHashSet();
        foreach (var key in _cache.Keys)
        {
            if (!listed.Contains(key))
            {
                _cache.TryRemove(key, out _);
            }
        }
    }
}
