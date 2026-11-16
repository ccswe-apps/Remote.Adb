using JetBrains.Annotations;

namespace Remote.Adb.Core.Adb.Wireless;

/// <summary>The outcome of switching a USB device to Wi-Fi debugging.</summary>
/// <param name="Outcome">How it ended.</param>
/// <param name="Address">The <c>ip:5555</c> address, once the device's Wi-Fi address was found.</param>
/// <param name="Message">adb's message for a failure, for display.</param>
[PublicAPI]
public sealed record WirelessEnableResult(WirelessEnableOutcome Outcome, string? Address, string Message)
{
    /// <summary>Whether the device is connected over Wi-Fi.</summary>
    public bool Success => Outcome == WirelessEnableOutcome.Connected;
}
