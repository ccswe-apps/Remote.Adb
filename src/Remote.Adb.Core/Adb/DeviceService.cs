namespace Remote.Adb.Core.Adb;

/// <inheritdoc />
internal sealed class DeviceService : IDeviceService
{
    private readonly IAdbRunner _adb;
    private readonly IDeviceDetailsResolver _detailsResolver;

    public DeviceService(IAdbRunner adb, IDeviceDetailsResolver detailsResolver)
    {
        _adb = adb;
        _detailsResolver = detailsResolver;
    }

    private async Task<AdbDevice> EnrichAsync(AdbDevice device, CancellationToken cancellationToken)
    {
        if (!device.IsOnline)
        {
            return device;
        }

        var details = await _detailsResolver.ResolveAsync(device, cancellationToken);
        return details is null
            ? device
            : device with { Name = details.Name, Form = details.Form, IsEmulator = details.IsEmulator, ApiLevel = details.ApiLevel, Abi = details.Abi };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AdbDevice>> ListAsync(CancellationToken cancellationToken = default)
    {
        // A non-zero exit is already logged by the process runner, with its exit code and output.
        var result = await _adb.RunAsync(["devices", "-l"], cancellationToken: cancellationToken);
        var devices = AdbOutputParser.ParseDeviceList(result.StandardOutput);

        // Forget details of connections that are gone, so a serial reused by another device is read afresh. A failed
        // listing says nothing about which devices are gone, so it keeps the cache.
        if (result.Success)
        {
            _detailsResolver.Retain(devices.Where(device => device.IsOnline).ToList());
        }

        // Enrich online devices with friendly details — cached per connection, so this is one getprop per device once.
        return await Task.WhenAll(devices.Select(device => EnrichAsync(device, cancellationToken)));
    }
}
