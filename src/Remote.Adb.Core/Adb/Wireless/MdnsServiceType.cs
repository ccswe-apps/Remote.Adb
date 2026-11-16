namespace Remote.Adb.Core.Adb.Wireless;

/// <summary>The adb mDNS service types a device can advertise.</summary>
public enum MdnsServiceType
{
    /// <summary><c>_adb-tls-connect._tcp</c> — a paired Android 11+ device's connect port.</summary>
    Connect,

    /// <summary><c>_adb-tls-pairing._tcp</c> — advertised only while the device's pairing dialog is open.</summary>
    Pairing,

    /// <summary><c>_adb._tcp</c> — a device in legacy <c>adb tcpip</c> mode.</summary>
    Legacy,

    /// <summary>Any other service type.</summary>
    Unknown,
}
