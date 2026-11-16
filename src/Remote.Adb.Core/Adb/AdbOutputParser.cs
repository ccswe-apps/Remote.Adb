using Remote.Adb.Core.Common;

namespace Remote.Adb.Core.Adb;

/// <summary>
/// Pure parsing of <c>adb</c> command output. Kept free of I/O so it is trivially unit-testable. Parses over
/// spans, allocating only the field strings it keeps.
/// </summary>
internal static class AdbOutputParser
{
    private const string DevicesHeader = "List of devices";

    /// <summary>
    /// Parses the output of <c>adb devices</c>, returning the serials of devices in the
    /// <c>device</c> (online) state. The header line and offline/unauthorized entries are skipped.
    /// </summary>
    public static IReadOnlyList<string> ParseDevices(string output) =>
        ParseDeviceList(output).Where(device => device.IsOnline).Select(device => device.Serial).ToList();

    /// <summary>
    /// Parses the output of <c>adb devices -l</c> into devices, preserving every connection state
    /// (<c>device</c>, <c>offline</c>, <c>unauthorized</c>, …). The trailing <c>key:value</c> columns
    /// (<c>model</c>, <c>product</c>, <c>device</c>, <c>transport_id</c>) are captured when present.
    /// </summary>
    public static IReadOnlyList<AdbDevice> ParseDeviceList(string output)
    {
        var devices = new List<AdbDevice>();

        foreach (var rawLine in output.AsSpan().EnumerateLines())
        {
            var line = rawLine.Trim();

            if (line.IsEmpty || line.StartsWith(DevicesHeader, StringComparison.Ordinal))
            {
                continue;
            }

            var rest = line;
            var serial = SpanTokenizer.NextToken(ref rest);
            var state = SpanTokenizer.NextToken(ref rest);

            if (serial.IsEmpty || state.IsEmpty)
            {
                continue;
            }

            string? model = null;
            string? product = null;
            string? device = null;
            string? transportId = null;

            for (var token = SpanTokenizer.NextToken(ref rest); !token.IsEmpty; token = SpanTokenizer.NextToken(ref rest))
            {
                var colon = token.IndexOf(':');
                if (colon <= 0)
                {
                    continue;
                }

                var key = token[..colon];
                var value = token[(colon + 1)..];

                if (key.SequenceEqual("model"))
                {
                    model = value.ToString();
                }
                else if (key.SequenceEqual("product"))
                {
                    product = value.ToString();
                }
                else if (key.SequenceEqual("device"))
                {
                    device = value.ToString();
                }
                else if (key.SequenceEqual("transport_id"))
                {
                    transportId = value.ToString();
                }
            }

            devices.Add(new AdbDevice(serial.ToString(), state.ToString(), model, product, device, transportId));
        }

        return devices;
    }
}
