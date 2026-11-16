using Remote.Adb.Core.Common;

namespace Remote.Adb.Core.Adb;

/// <inheritdoc />
public sealed class DeviceService : IDeviceService
{
    private readonly IAdbRunner _adb;
    private readonly IDeviceDetailsResolver _detailsResolver;

    public DeviceService(IAdbRunner adb, IDeviceDetailsResolver detailsResolver)
    {
        _adb = adb;
        _detailsResolver = detailsResolver;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AdbDevice>> ListAsync(CancellationToken cancellationToken = default)
    {
        // A non-zero exit is already logged by the process runner, with its exit code and output.
        var result = await _adb.RunAsync(["devices", "-l"], cancellationToken: cancellationToken);
        var devices = AdbOutputParser.ParseDeviceList(result.StandardOutput);

        // Enrich online devices with friendly details — cached by serial, so this is one getprop per device once.
        return await Task.WhenAll(devices.Select(device => EnrichAsync(device, cancellationToken)));
    }

    private async Task<AdbDevice> EnrichAsync(AdbDevice device, CancellationToken cancellationToken)
    {
        if (!device.IsOnline)
        {
            return device;
        }

        var details = await _detailsResolver.ResolveAsync(device.Serial, cancellationToken);
        return details is null
            ? device
            : device with { Name = details.Name, Form = details.Form, IsEmulator = details.IsEmulator, ApiLevel = details.ApiLevel, Abi = details.Abi };
    }
}
