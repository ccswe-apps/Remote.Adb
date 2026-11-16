using JetBrains.Annotations;

namespace Remote.Adb.Core.Emulators;

/// <summary>
/// Lists, starts, and stops Android emulators (AVDs).
/// </summary>
[PublicAPI]
public interface IEmulatorService
{
    /// <summary>
    /// Lists the available AVDs, annotated with whether each is currently running and its serial.
    /// </summary>
    Task<IReadOnlyList<AndroidVirtualDevice>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Launches the emulator for the given AVD name. Does not wait for it to finish booting.
    /// When <paramref name="coldBoot"/> is <see langword="true"/>, the saved snapshot is bypassed
    /// (<c>-no-snapshot-load</c>) so the AVD boots fresh.
    /// </summary>
    Task StartAsync(string avdName, bool coldBoot = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the running emulator with the given adb serial (e.g. <c>emulator-5554</c>). The result reports
    /// whether adb accepted the request and, if not, why (e.g. no such device).
    /// </summary>
    Task<AvdOperationResult> StopAsync(string serial, CancellationToken cancellationToken = default);
}
