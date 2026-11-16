using Microsoft.Extensions.Logging;
using Remote.Adb.Core.Common;
using Remote.Adb.Core.Logging;
using Remote.Adb.Core.Settings;

namespace Remote.Adb.Core.Adb.Wireless;

/// <inheritdoc />
public sealed class WirelessAdbService : IWirelessAdbService
{
    private const string DefaultPortSuffix = ":5555";

    private readonly IAdbRunner _adb;
    private readonly Lock _gate = new();
    private readonly ILogger<WirelessAdbService> _logger;
    private readonly ISettingsService _settings;

    public WirelessAdbService(IAdbRunner adb, ISettingsService settings, ILogger<WirelessAdbService> logger)
    {
        _adb = adb;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>
    /// How long one <c>adb connect</c> may take. adb's own timeout for an unreachable host is long; a stuck connect
    /// shouldn't hold the UI that long.
    /// </summary>
    internal TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>How long to keep trying to connect after switching a USB device to TCP mode (adbd restarts first).</summary>
    internal TimeSpan EnableConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>How long to wait, after pairing, for the device to connect.</summary>
    internal TimeSpan PairConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>How long <c>adb pair</c> itself may take; it blocks on the network handshake with no timeout of its own.</summary>
    internal TimeSpan PairTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How often to check for the device while waiting for it to connect after pairing.</summary>
    internal TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);

    private Task<ProcessResult> AdbAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        _adb.RunAsync(arguments, cancellationToken: cancellationToken);

    /// <inheritdoc />
    public async Task<WirelessConnectResult> ConnectAsync(string address, bool remember = true, CancellationToken cancellationToken = default)
    {
        var trimmed = address.Trim();
        if (trimmed.Length == 0)
        {
            return new WirelessConnectResult(WirelessConnectOutcome.InvalidAddress, "Enter the device's address, e.g. 192.168.1.20:5555.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ConnectTimeout);

        ProcessResult result;
        try
        {
            result = await AdbAsync(["connect", trimmed], timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new WirelessConnectResult(WirelessConnectOutcome.ConnectionFailed, $"Timed out connecting to {trimmed}.");
        }

        var connect = WirelessOutputParser.ParseConnect(result);
        if (connect.Success && remember)
        {
            Remember(trimmed);
        }

        return connect;
    }

    /// <inheritdoc />
    public async Task<WirelessResult> DisconnectAsync(string serial, CancellationToken cancellationToken = default)
    {
        var result = WirelessOutputParser.ParseDisconnect(await AdbAsync(["disconnect", serial], cancellationToken));

        // Forget it either way: an explicit disconnect means "stop reconnecting this", even if it was already gone.
        Forget(serial);
        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MdnsService>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var result = await AdbAsync(["mdns", "services"], cancellationToken);
        return result.Success ? WirelessOutputParser.ParseMdnsServices(result.StandardOutput) : [];
    }

    /// <inheritdoc />
    public async Task<WirelessEnableResult> EnableWirelessAsync(string serial, CancellationToken cancellationToken = default)
    {
        // The address must be read while USB still works: once adbd restarts in TCP mode the USB link drops.
        var ip = WirelessOutputParser.ParseIpRouteSource((await AdbAsync(["-s", serial, "shell", "ip", "route"], cancellationToken)).StandardOutput)
                 ?? WirelessOutputParser.ParseIpAddrInet((await AdbAsync(["-s", serial, "shell", "ip", "-4", "addr", "show", "wlan0"], cancellationToken)).StandardOutput);
        if (ip is null)
        {
            return new WirelessEnableResult(WirelessEnableOutcome.NoWifiAddress, null, "The device has no Wi-Fi address. Connect it to the same Wi-Fi network as this computer first.");
        }

        var address = ip + DefaultPortSuffix;
        var tcpip = WirelessOutputParser.ParseTcpip(await AdbAsync(["-s", serial, "tcpip", "5555"], cancellationToken));
        if (!tcpip.Success)
        {
            return new WirelessEnableResult(WirelessEnableOutcome.TcpipFailed, address, tcpip.Message);
        }

        WirelessConnectResult? last = null;
        var connected = await PollUntilAsync(EnableConnectTimeout, async token =>
        {
            last = await ConnectAsync(address, remember: true, token);
            return last.Success;
        }, cancellationToken);

        return connected
            ? new WirelessEnableResult(WirelessEnableOutcome.Connected, address, last!.Message)
            : new WirelessEnableResult(WirelessEnableOutcome.ConnectFailed, address, last?.Message ?? $"Timed out connecting to {address}.");
    }

    private void Forget(string serial)
    {
        lock (_gate)
        {
            var saved = _settings.WirelessAddresses;
            if (saved.Any(address => IsSameDevice(address, serial)))
            {
                _settings.WirelessAddresses = saved.Where(address => !IsSameDevice(address, serial)).ToList();
            }
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsDiscoveryAvailableAsync(CancellationToken cancellationToken = default)
    {
        var result = await AdbAsync(["mdns", "check"], cancellationToken);
        return result.Success && WirelessOutputParser.IsMdnsAvailable(result.StandardOutput);
    }

    // A saved address may omit adb's default port, while the serial adb reports always includes it.
    private static bool IsSameDevice(string address, string serial) =>
        string.Equals(address, serial, StringComparison.OrdinalIgnoreCase)
        || string.Equals(address + DefaultPortSuffix, serial, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<WirelessPairResult> PairAsync(string pairingAddress, string code, CancellationToken cancellationToken = default)
    {
        var address = pairingAddress.Trim();
        var pairingCode = code.Trim();
        if (address.Length == 0 || pairingCode.Length == 0)
        {
            return new WirelessPairResult(false, false, "Enter the pairing address and code shown on the device.");
        }

        // `adb pair <address>` with no code prompts for it on stdin — which keeps the code off the command line.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(PairTimeout);

        ProcessResult result;
        try
        {
            result = await _adb.RunAsync(["pair", address], $"{pairingCode}\n", timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new WirelessPairResult(false, false, $"Timed out pairing with {address}.");
        }

        var pair = WirelessOutputParser.ParsePair(result);
        if (!pair.Paired || pair.Guid is null)
        {
            return pair;
        }

        if (await WaitForConnectionAsync(pair.Guid, cancellationToken))
        {
            return pair with { Connected = true, Message = "Paired and connected." };
        }

        return pair with
        {
            Message = "Paired, but the device didn't connect automatically. Connect using the IP address and port shown under Wireless debugging.",
        };
    }

    /// <inheritdoc />
    public async Task<int> ReconnectSavedAsync(CancellationToken cancellationToken = default)
    {
        var addresses = _settings.WirelessAddresses;
        if (addresses.Count == 0)
        {
            return 0;
        }

        // Automatic, so the individual commands are routine background work; one summary line is logged instead.
        int connected;
        using (CommandLogScope.Background())
        {
            var results = await Task.WhenAll(addresses.Select(address => ConnectAsync(address, remember: false, cancellationToken)));
            connected = results.Count(result => result.Success);
        }

        _logger.LogInformation("Reconnected {Connected} of {Saved} saved wireless devices.", connected, addresses.Count);
        return connected;
    }

    // The shared "keep trying until it works or time runs out" loop: runs `attempt` every PollInterval as routine
    // background work, at least once, until it succeeds or `timeout` passes. An attempt still in flight at the deadline is
    // cancelled, so the whole wait is bounded by `timeout` (plus one interval).
    private async Task<bool> PollUntilAsync(TimeSpan timeout, Func<CancellationToken, Task<bool>> attempt, CancellationToken cancellationToken)
    {
        using var scope = CommandLogScope.Background();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        try
        {
            while (true)
            {
                if (await attempt(deadline.Token))
                {
                    return true;
                }

                await Task.Delay(PollInterval, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private void Remember(string address)
    {
        lock (_gate)
        {
            var saved = _settings.WirelessAddresses;
            if (!saved.Any(existing => IsSameDevice(existing, address) || IsSameDevice(address, existing)))
            {
                _settings.WirelessAddresses = [.. saved, address];
            }
        }
    }

    // After pairing, adb tries to connect by the device's mDNS name (its guid) — but only if it has already seen the
    // device's _adb-tls-connect announcement. Poll until the device is listed, connecting explicitly to the announced
    // address when it shows up but adb didn't connect on its own.
    private Task<bool> WaitForConnectionAsync(string guid, CancellationToken cancellationToken) =>
        PollUntilAsync(PairConnectTimeout, async token =>
        {
            var devices = await AdbAsync(["devices"], token);
            if (AdbOutputParser.ParseDevices(devices.StandardOutput).Any(serial => serial.StartsWith(guid, StringComparison.Ordinal)))
            {
                return true;
            }

            var service = (await DiscoverAsync(token))
                .FirstOrDefault(candidate => candidate.Type == MdnsServiceType.Connect && candidate.Instance == guid);
            return service is not null && (await ConnectAsync(service.Address, remember: false, token)).Success;
        }, cancellationToken);
}
