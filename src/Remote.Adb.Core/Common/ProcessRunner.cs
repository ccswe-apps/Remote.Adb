using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Remote.Adb.Core.Logging;

namespace Remote.Adb.Core.Common;

/// <inheritdoc />
public sealed class ProcessRunner : IProcessRunner
{
    // Large outputs (e.g. sdkmanager --list) are clipped in the log; callers still receive the full text.
    internal const int MaxLoggedOutputLength = 4096;

    private readonly ILogger<ProcessRunner> _logger;

    public ProcessRunner(ILogger<ProcessRunner> logger)
    {
        _logger = logger;
    }

    private static ProcessStartInfo CreateStartInfo(string fileName, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string>? environment = null)
    {
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // Overlay extra environment variables (e.g. JAVA_HOME for avdmanager/sdkmanager) onto the inherited block.
        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                startInfo.Environment[key] = value;
            }
        }

        // On Windows a .bat/.cmd (avdmanager, sdkmanager) isn't a valid executable for CreateProcess —
        // Process.Start throws Win32Exception. Run it through the command interpreter instead.
        if (OperatingSystem.IsWindows() && IsBatchScript(fileName))
        {
            startInfo.FileName = "cmd.exe";
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(fileName);
        }
        else
        {
            startInfo.FileName = fileName;
        }

        // ArgumentList handles quoting/escaping per-argument, avoiding command-line injection bugs.
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    // A readable one-line rendering for the log: the tool's name (not its full SDK path) and its arguments, quoting
    // any that contain whitespace.
    internal static string FormatCommand(string fileName, IReadOnlyList<string> arguments)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        if (arguments.Count == 0)
        {
            return name;
        }

        var rendered = arguments.Select(argument => argument.Any(char.IsWhiteSpace) ? $"\"{argument}\"" : argument);
        return $"{name} {string.Join(' ', rendered)}";
    }

    private static bool IsBatchScript(string fileName) =>
        fileName.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);

    private void LogCompleted(string command, ProcessResult result, long elapsedMilliseconds, bool background)
    {
        // Routine background work stays at Debug even when it fails, so a persistently failing poll can't flood the
        // log with a warning every few seconds; user-initiated failures surface as warnings.
        var level = background ? LogLevel.Debug : result.Success ? LogLevel.Information : LogLevel.Warning;
        ProcessRunnerLog.CommandCompleted(_logger, level, command, result.ExitCode, elapsedMilliseconds);

        // A user-initiated command's output is the point of the log, so it goes out alongside the completion
        // (Information, or Warning for a failure); background polls skip it — they re-read it every few seconds.
        if (background)
        {
            return;
        }

        var outputLevel = result.Success ? LogLevel.Information : LogLevel.Warning;
        LogOutput(outputLevel, command, "stdout", result.StandardOutput);
        LogOutput(outputLevel, command, "stderr", result.StandardError);
    }

    private void LogOutput(LogLevel level, string command, string stream, string output)
    {
        if (!_logger.IsEnabled(level))
        {
            return;
        }

        var trimmed = output.AsSpan().TrimEnd();
        if (trimmed.IsEmpty)
        {
            return;
        }

        var text = trimmed.Length > MaxLoggedOutputLength
            ? $"{trimmed[..MaxLoggedOutputLength]}… ({trimmed.Length - MaxLoggedOutputLength} more characters)"
            : trimmed.ToString();

        ProcessRunnerLog.CommandOutput(_logger, level, command, stream, text);
    }

    private static async Task ObserveAsync(Task task)
    {
        try
        {
            await task;
        }
        catch
        {
            // Swallow: we're already unwinding a cancellation; awaiting just marks the task observed.
        }
    }

    /// <inheritdoc />
    public async Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string? standardInput = null, IReadOnlyDictionary<string, string>? environment = null, CancellationToken cancellationToken = default)
    {
        var startInfo = CreateStartInfo(fileName, arguments, environment);
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.RedirectStandardInput = standardInput is not null;

        var command = FormatCommand(fileName, arguments);
        var background = CommandLogScope.IsBackground;
        ProcessRunnerLog.CommandStarted(_logger, LogLevel.Debug, command);

        var stopwatch = Stopwatch.StartNew();
        using var process = new Process { StartInfo = startInfo };
        StartProcess(process, fileName, command, background);

        if (standardInput is not null)
        {
            // Feed the prompt response (e.g. avdmanager's "create a custom hardware profile? [no]") then
            // close stdin so the tool stops waiting on input. A tool that errors out before reading stdin
            // (bad package, missing JDK) closes the pipe early, so a broken-pipe write here is benign — let
            // the non-zero exit code carry the failure instead of throwing an IOException out of RunAsync.
            try
            {
                await process.StandardInput.WriteAsync(standardInput);
                process.StandardInput.Close();
            }
            catch (IOException)
            {
            }
        }

        // Read both streams concurrently and only then wait for exit, otherwise a process that
        // fills the stderr pipe buffer while we drain stdout (or vice versa) would deadlock.
        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);

            var standardOutput = await standardOutputTask;
            var standardError = await standardErrorTask;
            var result = new ProcessResult(process.ExitCode, standardOutput, standardError);

            LogCompleted(command, result, stopwatch.ElapsedMilliseconds, background);
            return result;
        }
        catch (OperationCanceledException)
        {
            // Cancellation must not leave the spawned tool (a JVM/emulator) running detached. Kill the whole
            // tree, then observe the stream reads — which cancel too — so they don't surface later as
            // unobserved-task exceptions, before propagating the cancellation.
            process.KillTree();
            await ObserveAsync(standardOutputTask);
            await ObserveAsync(standardErrorTask);

            ProcessRunnerLog.CommandCancelled(_logger, background ? LogLevel.Debug : LogLevel.Information, command, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    /// <inheritdoc />
    public Process Start(string fileName, IReadOnlyList<string> arguments)
    {
        var startInfo = CreateStartInfo(fileName, arguments);

        var command = FormatCommand(fileName, arguments);
        var background = CommandLogScope.IsBackground;
        ProcessRunnerLog.CommandStarted(_logger, background ? LogLevel.Debug : LogLevel.Information, command);

        var process = new Process { StartInfo = startInfo };
        StartProcess(process, fileName, command, background);

        return process;
    }

    private void StartProcess(Process process, string fileName, string command, bool background)
    {
        try
        {
            process.Start();
        }
        catch (Win32Exception exception)
        {
            var launchException = new ProcessLaunchException(fileName, exception);
            ProcessRunnerLog.CommandLaunchFailed(_logger, background ? LogLevel.Debug : LogLevel.Warning, command, launchException);
            throw launchException;
        }
    }

    /// <inheritdoc />
    public IProcessSession StartSession(string fileName, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string>? environment = null)
    {
        var startInfo = CreateStartInfo(fileName, arguments, environment);
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;

        var command = FormatCommand(fileName, arguments);
        var background = CommandLogScope.IsBackground;
        ProcessRunnerLog.CommandStarted(_logger, background ? LogLevel.Debug : LogLevel.Information, command);

        var process = new Process { StartInfo = startInfo };
        StartProcess(process, fileName, command, background);

        return new ProcessSession(process);
    }
}
