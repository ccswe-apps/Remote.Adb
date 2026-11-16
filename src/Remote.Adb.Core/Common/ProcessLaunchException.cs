using JetBrains.Annotations;

namespace Remote.Adb.Core.Common;

/// <summary>
/// Thrown when an external tool cannot be launched — typically because the executable is not
/// installed or not on <c>PATH</c> (e.g. the Android SDK is missing).
/// </summary>
[PublicAPI]
public sealed class ProcessLaunchException : Exception
{
    /// <summary>Creates the exception for <paramref name="fileName"/>, wrapping the OS launch failure.</summary>
    /// <param name="fileName">The executable that couldn't be started.</param>
    /// <param name="innerException">The underlying launch failure (e.g. a <c>Win32Exception</c>).</param>
    public ProcessLaunchException(string fileName, Exception innerException)
        : base($"Could not start '{fileName}'. Ensure it is installed and on PATH (set ANDROID_HOME for the Android SDK tools).", innerException)
    {
        FileName = fileName;
    }

    /// <summary>The executable that couldn't be started.</summary>
    public string FileName { get; }
}
