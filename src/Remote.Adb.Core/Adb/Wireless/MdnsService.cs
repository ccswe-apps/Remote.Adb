namespace Remote.Adb.Core.Adb.Wireless;

/// <summary>One adb service the local adb server discovered over mDNS (a line of <c>adb mdns services</c>).</summary>
public sealed record MdnsService(string Instance, MdnsServiceType Type, string Host, int Port)
{
    /// <summary>The <c>host:port</c> to connect or pair to.</summary>
    public string Address => $"{Host}:{Port}";
}
