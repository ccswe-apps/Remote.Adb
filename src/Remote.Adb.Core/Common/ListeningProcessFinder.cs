namespace Remote.Adb.Core.Common;

/// <summary>Picks the <see cref="IListeningProcessFinder"/> for the current OS.</summary>
internal static class ListeningProcessFinder
{
    /// <summary>Creates the finder for this platform: the TCP table on Windows, <c>lsof</c> on macOS, <c>/proc</c> elsewhere.</summary>
    /// <param name="processRunner">Runs <c>lsof</c> on macOS.</param>
    public static IListeningProcessFinder Create(IProcessRunner processRunner)
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsListeningProcessFinder();
        }

        return OperatingSystem.IsMacOS()
            ? new MacListeningProcessFinder(processRunner)
            : new LinuxListeningProcessFinder();
    }
}
