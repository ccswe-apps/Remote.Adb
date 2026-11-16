using System.Diagnostics;

namespace Remote.Adb.Core.Common;

/// <inheritdoc />
internal sealed class ProcessSession : IProcessSession
{
    private bool _disposed;
    private int? _exitCode;
    private readonly Process _process;
    private string _standardError = string.Empty;
    private readonly Task<string> _standardErrorTask;
    private readonly Task<string> _standardOutputTask;

    public ProcessSession(Process process)
    {
        _process = process;

        // Drain both pipes from the start: a process that fills the stderr buffer while we ignore it (or vice
        // versa) would block forever. stdout is drained but discarded — ssh -N writes its diagnostics to stderr.
        _standardOutputTask = process.StandardOutput.ReadToEndAsync();
        _standardErrorTask = process.StandardError.ReadToEndAsync();
    }

    /// <inheritdoc />
    public int? ExitCode => _exitCode ?? (_disposed ? null : TryReadExitCode());

    /// <inheritdoc />
    public bool HasExited => _disposed || ExitCode is not null;

    /// <inheritdoc />
    public string StandardError => _standardError;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        Kill();

        try
        {
            await Task.WhenAll(_standardOutputTask, _standardErrorTask);
        }
        catch
        {
            // Killing the process tears the pipes down; the drain reads completing in error is expected.
        }

        if (_standardErrorTask.IsCompletedSuccessfully)
        {
            _standardError = _standardErrorTask.Result;
        }

        // Capture the exit code while the Process can still be asked: once disposed it throws instead.
        _exitCode ??= TryReadExitCode();
        _disposed = true;
        _process.Dispose();
    }

    /// <inheritdoc />
    public void Kill()
    {
        if (!_disposed)
        {
            _process.KillTree();
        }
    }

    // The exit code if the process has exited, else null — null too if the Process can no longer be queried
    // (disposed concurrently).
    private int? TryReadExitCode()
    {
        try
        {
            return _process.HasExited ? _process.ExitCode : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<int> WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        if (!_disposed)
        {
            await _process.WaitForExitAsync(cancellationToken);

            _standardError = await _standardErrorTask;
            await _standardOutputTask;

            _exitCode ??= TryReadExitCode();
        }

        // -1 when the session was disposed before the exit code could be read (it was killed).
        return _exitCode ?? -1;
    }
}
