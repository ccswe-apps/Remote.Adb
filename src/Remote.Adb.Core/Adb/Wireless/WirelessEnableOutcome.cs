using JetBrains.Annotations;

namespace Remote.Adb.Core.Adb.Wireless;

/// <summary>How switching a USB device to Wi-Fi debugging (<c>adb tcpip</c> then <c>adb connect</c>) ended.</summary>
[PublicAPI]
public enum WirelessEnableOutcome
{
    /// <summary>The device is in TCP mode and connected over Wi-Fi; it can be unplugged.</summary>
    Connected,

    /// <summary>The device has no Wi-Fi address (not on Wi-Fi), so nothing was changed.</summary>
    NoWifiAddress,

    /// <summary><c>adb tcpip</c> failed, so the device is still in USB mode.</summary>
    TcpipFailed,

    /// <summary>
    /// The device switched to TCP mode but couldn't be reached over Wi-Fi — a partial success: it's listening on port
    /// 5555 but this computer can't see it (a different network, or a firewall).
    /// </summary>
    ConnectFailed,
}
