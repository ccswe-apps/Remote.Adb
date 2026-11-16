namespace Remote.Adb.Core.Adb;

/// <summary>
/// Builds <see cref="DeviceDetails"/> from a device's <c>getprop</c> output. Pure (no I/O), so it's
/// unit-testable. An emulator is named after the AVD it runs (e.g. <c>Pixel_9</c> → "Pixel 9"); a physical device
/// prefers its marketing name, else its model. The form factor comes from <c>ro.build.characteristics</c>.
/// </summary>
internal static class DeviceDetailsParser
{
    public static DeviceDetails Build(string serial, string getpropOutput)
    {
        var properties = Parse(getpropOutput);

        var characteristics = ParseCharacteristics(properties.GetValueOrDefault("ro.build.characteristics", string.Empty));

        var isEmulator = properties.GetValueOrDefault("ro.kernel.qemu") == "1"
            || characteristics.Contains("emulator")
            || serial.StartsWith("emulator-", StringComparison.Ordinal);

        var form = characteristics.Contains("tablet") ? DeviceForm.Tablet
            : characteristics.Contains("watch") ? DeviceForm.Watch
            : characteristics.Contains("automotive") ? DeviceForm.Automotive
            : characteristics.Contains("tv") ? DeviceForm.Television
            : DeviceForm.Phone;

        var name = isEmulator
            ? Clean(FirstNonEmpty(properties, "ro.boot.qemu.avd_name", "ro.product.model"))
            : Clean(FirstNonEmpty(properties, "ro.product.marketing.name", "ro.product.vendor.marketname", "ro.product.model"));

        var apiLevel = int.TryParse(properties.GetValueOrDefault("ro.build.version.sdk"), out var sdk) ? sdk : (int?)null;
        var abi = FirstNonEmpty(properties, "ro.product.cpu.abi");

        return new DeviceDetails(string.IsNullOrEmpty(name) ? null : name, form, isEmulator, apiLevel, abi);
    }

    // adb/AVD names sanitize spaces to underscores ("Pixel_9" → "Pixel 9"); undo that.
    private static string? Clean(string? value) => value?.Replace('_', ' ');

    private static string? FirstNonEmpty(IReadOnlyDictionary<string, string> properties, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (properties.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        return null;
    }

    private static Dictionary<string, string> Parse(string output)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var rawLine in output.AsSpan().EnumerateLines())
        {
            var line = rawLine.Trim();
            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            properties[line[..separator].ToString()] = line[(separator + 1)..].Trim().ToString();
        }

        return properties;
    }

    // ro.build.characteristics is a comma-separated list (e.g. "emulator,tablet"); split it once.
    private static HashSet<string> ParseCharacteristics(string characteristics)
    {
        var source = characteristics.AsSpan();
        var parts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var range in source.Split(','))
        {
            var part = source[range].Trim();
            if (!part.IsEmpty)
            {
                parts.Add(part.ToString());
            }
        }

        return parts;
    }
}
