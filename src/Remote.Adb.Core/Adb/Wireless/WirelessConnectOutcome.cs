namespace Remote.Adb.Core.Adb.Wireless;

/// <summary>How an <c>adb connect</c> attempt ended, classified from its output (it exits 0 even on failure).</summary>
public enum WirelessConnectOutcome
{
    /// <summary><c>connected to …</c></summary>
    Connected,

    /// <summary><c>already connected to …</c></summary>
    AlreadyConnected,

    /// <summary><c>failed to authenticate to …</c> — the device rejected (or hasn't yet accepted) this host's key.</summary>
    AuthenticationFailed,

    /// <summary><c>failed to connect to …</c> / <c>cannot connect to …</c> — nothing answered at that address.</summary>
    ConnectionFailed,

    /// <summary><c>cannot resolve host …</c> / <c>failed to resolve host …</c></summary>
    ResolveFailed,

    /// <summary>The address was empty or malformed.</summary>
    InvalidAddress,

    /// <summary>adb itself failed (non-zero exit) — typically the adb server couldn't be reached or started.</summary>
    ServerUnavailable,

    /// <summary>Output adb produced that none of the known messages match.</summary>
    Unknown,
}
