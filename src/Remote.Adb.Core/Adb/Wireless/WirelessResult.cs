using JetBrains.Annotations;

namespace Remote.Adb.Core.Adb.Wireless;

/// <summary>A wireless adb operation's success and adb's message for display.</summary>
[PublicAPI]
public sealed record WirelessResult(bool Success, string Message);
