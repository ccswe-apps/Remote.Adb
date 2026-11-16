namespace Remote.Adb.Core.Common;

/// <summary>
/// The captured outcome of running an external process to completion.
/// </summary>
internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    /// <summary>
    /// <see langword="true"/> when the process exited with code 0.
    /// </summary>
    public bool Success => ExitCode == 0;

    /// <summary>
    /// The most useful text for reporting what <paramref name="tool"/> printed — stderr (where tools put errors) when
    /// there is any, else stdout, else <c>"&lt;tool&gt; exited with code N"</c>. The shared way to turn a process result
    /// into a user-facing message.
    /// </summary>
    public string Describe(string tool)
    {
        var error = StandardError.Trim();
        if (error.Length > 0)
        {
            return error;
        }

        var output = StandardOutput.Trim();
        return output.Length > 0 ? output : $"{tool} exited with code {ExitCode}";
    }
}
