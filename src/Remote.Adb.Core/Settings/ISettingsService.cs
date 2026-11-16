using JetBrains.Annotations;


namespace Remote.Adb.Core.Settings;

/// <summary>
/// Stores user-configurable application settings. Shared by both front-ends so a setting
/// changed in one place is observed everywhere.
/// </summary>
[PublicAPI]
public interface ISettingsService
{
    /// <summary>Override for the AVD home directory, or <see langword="null"/> to use env-var/default resolution.</summary>
    string? AvdHome { get; set; }

    /// <summary>The selected application layout density.</summary>
    AppDensity Density { get; set; }

    /// <summary>Override for the Java home (JDK) directory, or <see langword="null"/> to use <c>JAVA_HOME</c>/PATH.</summary>
    string? JavaHome { get; set; }

    /// <summary>The height, in pixels, of the desktop app's docked log panel when it's open.</summary>
    double LogPanelHeight { get; set; }

    /// <summary>Whether the desktop app's docked log panel is open.</summary>
    bool LogPanelOpen { get; set; }

    /// <summary>Override for the Android SDK root, or <see langword="null"/> to use env-var/default resolution.</summary>
    string? SdkRoot { get; set; }

    /// <summary>The selected application color theme.</summary>
    AppTheme Theme { get; set; }

    /// <summary>Whether to open the SSH reverse tunnel automatically when the app launches.</summary>
    bool TunnelAutoConnect { get; set; }

    /// <summary>The remote SSH host the reverse tunnel connects to, or <see langword="null"/> if not configured.</summary>
    string? TunnelHost { get; set; }

    /// <summary>The local port the tunnel forwards back to (the local adb server port).</summary>
    int TunnelLocalPort { get; set; }

    /// <summary>The remote port the tunnel binds (the port the remote adb client connects to).</summary>
    int TunnelRemotePort { get; set; }

    /// <summary>Wireless device addresses (<c>host:port</c>) to reconnect whenever the local adb server restarts.</summary>
    IReadOnlyList<string> WirelessAddresses { get; set; }
}
