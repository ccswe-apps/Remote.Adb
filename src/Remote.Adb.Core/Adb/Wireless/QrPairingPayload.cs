using System.Security.Cryptography;
using JetBrains.Annotations;

namespace Remote.Adb.Core.Adb.Wireless;

/// <summary>
/// What an Android 11+ device's "Pair device with QR code" scanner reads: a service name the device then advertises
/// over mDNS (as a <c>_adb-tls-pairing._tcp</c> instance) and the password it pairs with. Pairing then works exactly
/// like a pairing code, with the password as the code.
/// </summary>
/// <param name="ServiceName">The mDNS instance name the device will advertise once it scans the code.</param>
/// <param name="Password">The pairing password (the "code").</param>
[PublicAPI]
public sealed record QrPairingPayload(string ServiceName, string Password)
{
    // Letters and digits only: the payload is ';'/':'-delimited, adb sends "host:pair:<password>:<address>", and a '.'
    // in an mDNS instance name breaks name parsing.
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    private const string ServiceNamePrefix = "RemoteAdb";

    /// <summary>The text encoded in the QR code: <c>WIFI:T:ADB;S:&lt;name&gt;;P:&lt;password&gt;;;</c>.</summary>
    public string Text => $"WIFI:T:ADB;S:{ServiceName};P:{Password};;";

    /// <summary>A fresh payload with a random service name and password.</summary>
    public static QrPairingPayload Create() =>
        new(ServiceNamePrefix + RandomNumberGenerator.GetString(Alphabet, 8), RandomNumberGenerator.GetString(Alphabet, 12));
}
