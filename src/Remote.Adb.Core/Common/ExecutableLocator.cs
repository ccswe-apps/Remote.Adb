namespace Remote.Adb.Core.Common;

/// <summary>
/// Locates an executable on the <c>PATH</c> — used to detect whether a tool (e.g. <c>java</c>) is available
/// without launching it.
/// </summary>
internal static class ExecutableLocator
{
    /// <summary>
    /// The platform file name of the executable <paramref name="name"/>: <c>name.exe</c> on Windows, the bare name
    /// elsewhere — for building the path of a tool inside a known directory (an SDK or JDK <c>bin</c>).
    /// </summary>
    /// <param name="name">The tool name without extension (e.g. <c>java</c>).</param>
    public static string ExecutableName(string name) => OperatingSystem.IsWindows() ? $"{name}.exe" : name;

    /// <summary>
    /// Returns the full path to <paramref name="fileName"/> found on <c>PATH</c> (trying the platform's
    /// executable extensions on Windows), or <see langword="null"/> if it isn't on <c>PATH</c>.
    /// </summary>
    public static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        var names = ExecutableNames(fileName);

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static IReadOnlyList<string> ExecutableNames(string fileName)
    {
        // On non-Windows (and when the caller already gave an extension) the name is used as-is; otherwise try
        // the common Windows executable extensions.
        if (!OperatingSystem.IsWindows() || Path.HasExtension(fileName))
        {
            return [fileName];
        }

        return [fileName + ".exe", fileName + ".bat", fileName + ".cmd"];
    }
}
