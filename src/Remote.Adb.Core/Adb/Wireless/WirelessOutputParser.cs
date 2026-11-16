using Remote.Adb.Core.Common;

namespace Remote.Adb.Core.Adb.Wireless;

/// <summary>
/// Pure parsing of adb's wireless-debugging commands (<c>connect</c>, <c>disconnect</c>, <c>pair</c>,
/// <c>mdns services</c>). The messages are adb's own (AOSP <c>client/transport_emulator.cpp</c>,
/// <c>client/adb_wifi.cpp</c>, <c>client/transport_mdns.cpp</c>); parsing is over spans, allocating only what's kept.
/// </summary>
internal static class WirelessOutputParser
{
    // The tool name in a failure message from ProcessResult.Describe.
    private const string Adb = "adb";
    private const string MdnsHeader = "List of discovered mdns services";
    private const string PairedPrefix = "Successfully paired to ";
    private const string PairingPrompt = "Enter pairing code:";

    private static ReadOnlySpan<char> FirstLine(string text)
    {
        foreach (var line in text.AsSpan().EnumerateLines())
        {
            var trimmed = line.Trim();
            if (!trimmed.IsEmpty)
            {
                return trimmed;
            }
        }

        return [];
    }

    /// <summary>
    /// Whether <c>adb mdns check</c> reports working discovery. adb exits 0 either way; it prints
    /// <c>mdns daemon version …</c> when discovery works and <c>ERROR: …</c> when it's disabled or unavailable.
    /// </summary>
    public static bool IsMdnsAvailable(string output) =>
        FirstLine(output).StartsWith("mdns daemon version", StringComparison.Ordinal);

    /// <summary>
    /// Classifies <c>adb connect</c>. adb exits 0 whenever its server answered — even when the connection failed —
    /// so the outcome comes from the message; a non-zero exit means adb couldn't reach (or start) its server.
    /// </summary>
    public static WirelessConnectResult ParseConnect(ProcessResult result)
    {
        if (!result.Success)
        {
            return new WirelessConnectResult(WirelessConnectOutcome.ServerUnavailable, result.Describe(Adb));
        }

        var line = FirstLine(result.StandardOutput);
        if (line.IsEmpty)
        {
            line = FirstLine(result.StandardError);
        }

        // "already connected to" contains "connected to", so it must be tested first.
        var outcome = line switch
        {
            _ when line.StartsWith("already connected to", StringComparison.Ordinal) => WirelessConnectOutcome.AlreadyConnected,
            _ when line.StartsWith("connected to", StringComparison.Ordinal) => WirelessConnectOutcome.Connected,
            _ when line.StartsWith("failed to authenticate to", StringComparison.Ordinal) => WirelessConnectOutcome.AuthenticationFailed,
            _ when line.StartsWith("cannot resolve host", StringComparison.Ordinal)
                   || line.StartsWith("failed to resolve host", StringComparison.Ordinal) => WirelessConnectOutcome.ResolveFailed,
            _ when line.StartsWith("failed to connect to", StringComparison.Ordinal)
                   || line.StartsWith("cannot connect to", StringComparison.Ordinal) => WirelessConnectOutcome.ConnectionFailed,
            _ when line.StartsWith("empty address", StringComparison.Ordinal) => WirelessConnectOutcome.InvalidAddress,
            _ => WirelessConnectOutcome.Unknown,
        };

        return new WirelessConnectResult(outcome, line.ToString());
    }

    /// <summary>
    /// Parses <c>adb disconnect</c>: <c>disconnected …</c> (exit 0) on success, <c>error: no such device …</c> (exit 1)
    /// when the serial isn't connected.
    /// </summary>
    public static WirelessResult ParseDisconnect(ProcessResult result)
    {
        var line = FirstLine(result.StandardOutput);
        var success = result.Success && line.StartsWith("disconnected", StringComparison.Ordinal);
        return new WirelessResult(success, success ? line.ToString() : result.Describe(Adb));
    }

    /// <summary>
    /// Parses the output of <c>adb -s &lt;serial&gt; shell ip -4 addr show wlan0</c> for the interface's IPv4 address
    /// (the <c>inet a.b.c.d/nn</c> line, without the prefix length); <see langword="null"/> when it has none.
    /// </summary>
    public static string? ParseIpAddrInet(string output)
    {
        foreach (var rawLine in output.AsSpan().EnumerateLines())
        {
            var address = SpanTokenizer.TokenAfter(rawLine, "inet");
            if (address.IsEmpty)
            {
                continue;
            }

            var slash = address.IndexOf('/');
            return (slash >= 0 ? address[..slash] : address).ToString();
        }

        return null;
    }

    /// <summary>
    /// Parses the output of <c>adb -s &lt;serial&gt; shell ip route</c> for the device's Wi-Fi address: the <c>src</c> of
    /// a route on a <c>wlan*</c> interface. Other interfaces (mobile data, a VPN) are ignored — the device must be reached
    /// over the same Wi-Fi as this computer — so <see langword="null"/> means "not on Wi-Fi" (or a non-standard name).
    /// </summary>
    public static string? ParseIpRouteSource(string output)
    {
        foreach (var rawLine in output.AsSpan().EnumerateLines())
        {
            var source = SpanTokenizer.TokenAfter(rawLine, "src");
            if (!source.IsEmpty && SpanTokenizer.TokenAfter(rawLine, "dev").StartsWith("wlan", StringComparison.Ordinal))
            {
                return source.ToString();
            }
        }

        return null;
    }

    /// <summary>
    /// Parses <c>adb mdns services</c>: a header line, then one tab-separated
    /// <c>instance&lt;TAB&gt;type&lt;TAB&gt;ip:port</c> line per service. Unparseable lines are skipped.
    /// </summary>
    public static IReadOnlyList<MdnsService> ParseMdnsServices(string output)
    {
        var services = new List<MdnsService>();

        foreach (var rawLine in output.AsSpan().EnumerateLines())
        {
            var line = rawLine.Trim();
            if (line.IsEmpty || line.StartsWith(MdnsHeader, StringComparison.Ordinal))
            {
                continue;
            }

            // Instance names can contain spaces (a QR pairing name, a user-named device), so split on tabs only.
            var firstTab = line.IndexOf('\t');
            if (firstTab <= 0)
            {
                continue;
            }

            var instance = line[..firstTab].Trim();
            var rest = line[(firstTab + 1)..];
            var secondTab = rest.IndexOf('\t');
            if (secondTab <= 0)
            {
                continue;
            }

            var type = rest[..secondTab].Trim();
            var address = rest[(secondTab + 1)..].Trim();
            var colon = address.LastIndexOf(':');
            if (colon <= 0 || !int.TryParse(address[(colon + 1)..], out var port))
            {
                continue;
            }

            services.Add(new MdnsService(instance.ToString(), ParseServiceType(type), address[..colon].ToString(), port));
        }

        return services;
    }

    /// <summary>
    /// Parses <c>adb pair</c>. Success is exit 0 plus <c>Successfully paired to &lt;host&gt; [guid=&lt;guid&gt;]</c> — the
    /// line may follow adb's <c>Enter pairing code:</c> prompt when the code was supplied on stdin. adb reports most
    /// pairing failures as a generic <c>error: protocol fault …</c>, which is passed through.
    /// </summary>
    public static WirelessPairResult ParsePair(ProcessResult result)
    {
        var output = result.StandardOutput.AsSpan();
        var paired = output.IndexOf(PairedPrefix, StringComparison.Ordinal);

        if (!result.Success || paired < 0)
        {
            return new WirelessPairResult(false, false, StripPrompt(result.Describe(Adb)));
        }

        var line = FirstLine(output[paired..].ToString());
        string? guid = null;
        var guidStart = line.IndexOf("[guid=", StringComparison.Ordinal);
        if (guidStart >= 0)
        {
            var value = line[(guidStart + "[guid=".Length)..];
            var end = value.IndexOf(']');
            guid = (end >= 0 ? value[..end] : value).Trim().ToString();
        }

        return new WirelessPairResult(true, false, line.ToString(), string.IsNullOrEmpty(guid) ? null : guid);
    }

    private static MdnsServiceType ParseServiceType(ReadOnlySpan<char> type)
    {
        // adb may print the type with a trailing "." or ".local." — compare on the bare "_name._tcp".
        var local = type.IndexOf(".local", StringComparison.Ordinal);
        var bare = (local >= 0 ? type[..local] : type).TrimEnd('.');

        return bare switch
        {
            "_adb-tls-connect._tcp" => MdnsServiceType.Connect,
            "_adb-tls-pairing._tcp" => MdnsServiceType.Pairing,
            "_adb._tcp" => MdnsServiceType.Legacy,
            _ => MdnsServiceType.Unknown,
        };
    }

    /// <summary>
    /// Parses <c>adb -s &lt;serial&gt; tcpip &lt;port&gt;</c>: success when adbd switched to TCP
    /// (<c>restarting in TCP mode port: …</c>), otherwise adb's error.
    /// </summary>
    public static WirelessResult ParseTcpip(ProcessResult result)
    {
        var line = FirstLine(result.StandardOutput);
        var success = result.Success && line.StartsWith("restarting in TCP mode", StringComparison.Ordinal);
        return new WirelessResult(success, success ? line.ToString() : result.Describe(Adb));
    }

    private static string StripPrompt(string message) =>
        message.StartsWith(PairingPrompt, StringComparison.Ordinal) ? message[PairingPrompt.Length..].Trim() : message;
}
