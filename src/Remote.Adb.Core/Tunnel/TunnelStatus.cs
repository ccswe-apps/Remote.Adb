using JetBrains.Annotations;

namespace Remote.Adb.Core.Tunnel;

/// <summary>
/// A snapshot of the tunnel's <see cref="State"/> with an optional human-readable <see cref="Message"/>
/// (the forward description when connected, or the failure detail when faulted).
/// </summary>
[PublicAPI]
public sealed record TunnelStatus(TunnelState State, string? Message);
