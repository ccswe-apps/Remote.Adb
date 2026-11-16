namespace Remote.Adb.Core.Adb.Wireless;

/// <summary>
/// Connects Android devices over the network through the <b>local</b> adb server — the one the SSH tunnel exposes to
/// the remote host, so a device connected here is visible there too.
/// </summary>
public interface IWirelessAdbService
{
    /// <summary>
    /// Connects to a device at <paramref name="address"/> (<c>host[:port]</c>; adb defaults the port to 5555), passed
    /// to adb unchanged. When <paramref name="remember"/> is set, a successful connection is saved so it's restored
    /// after the adb server restarts.
    /// </summary>
    Task<WirelessConnectResult> ConnectAsync(string address, bool remember = true, CancellationToken cancellationToken = default);

    /// <summary>Disconnects the network device <paramref name="serial"/> and forgets it if it was saved.</summary>
    Task<WirelessResult> DisconnectAsync(string serial, CancellationToken cancellationToken = default);

    /// <summary>Lists the adb services the local adb server has discovered over mDNS (<c>adb mdns services</c>).</summary>
    Task<IReadOnlyList<MdnsService>> DiscoverAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the local adb server can discover devices over mDNS (<c>adb mdns check</c>) — needed for the discovered-device
    /// lists and QR pairing.
    /// </summary>
    Task<bool> IsDiscoveryAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Pairs with a device whose Wireless debugging "Pair device with pairing code" dialog shows
    /// <paramref name="pairingAddress"/> and <paramref name="code"/>, then waits briefly for it to connect —
    /// connecting explicitly via its mDNS-advertised address if adb didn't do so automatically. The code is passed on
    /// stdin, never on the command line, so it doesn't appear in logs or the process list.
    /// </summary>
    Task<WirelessPairResult> PairAsync(string pairingAddress, string code, CancellationToken cancellationToken = default);

    /// <summary>Reconnects every saved address, returning how many are connected afterwards.</summary>
    Task<int> ReconnectSavedAsync(CancellationToken cancellationToken = default);
}
