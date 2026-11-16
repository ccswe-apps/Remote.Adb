using System.ComponentModel;
using System.Diagnostics;

namespace Remote.Adb.Core.Common;

internal static class ProcessExtensions
{
    /// <summary>
    /// Kills the process and its descendants if it is still running. Safe on an already-exited or unkillable
    /// process — the platform exceptions that signal "nothing to kill" are swallowed.
    /// </summary>
    public static void KillTree(this Process process) => process.Terminate(entireProcessTree: true);

    /// <summary>
    /// Kills the process — and, when <paramref name="entireProcessTree"/> is set, its descendants — if it is still
    /// running. Safe on an already-exited or unkillable process: the platform exceptions that signal "nothing to kill"
    /// are swallowed, including the <see cref="AggregateException"/> a tree kill throws when some descendants couldn't
    /// be killed.
    /// </summary>
    public static void Terminate(this Process process, bool entireProcessTree)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree);
            }
        }
        catch (Exception exception) when (exception is AggregateException or InvalidOperationException or Win32Exception or NotSupportedException)
        {
        }
    }
}
