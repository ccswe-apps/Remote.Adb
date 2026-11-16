namespace Remote.Adb.Core.Adb.Wireless;

/// <summary>
/// The outcome of pairing with a device (Android 11+ wireless debugging). Pairing and connecting are separate steps:
/// a device can pair (<see cref="Paired"/>) yet not connect, e.g. when this machine can't see its mDNS
/// announcement — then <see cref="Connected"/> is false and the device must be connected by address.
/// </summary>
/// <param name="Paired">Whether <c>adb pair</c> succeeded.</param>
/// <param name="Connected">Whether the device is connected after pairing.</param>
/// <param name="Message">A message for display: adb's error, or what happened.</param>
/// <param name="Guid">The device's adb guid (also its mDNS service instance name), when paired.</param>
public sealed record WirelessPairResult(bool Paired, bool Connected, string Message, string? Guid = null);
