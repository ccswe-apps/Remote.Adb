using JetBrains.Annotations;

namespace Remote.Adb.Core.Adb.Wireless;

/// <summary>The outcome of an <c>adb connect</c>, with adb's own message for display.</summary>
[PublicAPI]
public sealed record WirelessConnectResult(WirelessConnectOutcome Outcome, string Message)
{
    /// <summary>Whether the device is connected now (newly or already).</summary>
    public bool Success => Outcome is WirelessConnectOutcome.Connected or WirelessConnectOutcome.AlreadyConnected;
}
