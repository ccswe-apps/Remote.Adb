using Remote.Adb.Core.Common;

namespace Remote.Adb.Testing;

/// <summary>
/// A controllable <see cref="IProcessSession"/>. Construct it already-exited (a lost bind race) or leave it
/// running and later <see cref="Exit"/> it (a tunnel that drops); <see cref="KillCount"/> records teardown.
/// Models disposal the way <see cref="ProcessSession"/> does: disposing kills a running session, a disposed
/// session reports <see cref="HasExited"/> and keeps its <see cref="ExitCode"/>, and <see cref="Kill"/> after
/// disposal is a no-op.
/// </summary>
internal sealed class FakeProcessSession : IProcessSession
{
    private readonly TaskCompletionSource<int> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public FakeProcessSession(int? immediateExitCode = null, string standardError = "")
    {
        StandardError = standardError;

        if (immediateExitCode is not null)
        {
            ExitCode = immediateExitCode;
            _exit.SetResult(immediateExitCode.Value);
        }
    }

    /// <summary>How many times <see cref="DisposeAsync"/> was called.</summary>
    public int DisposeCount { get; private set; }

    public int? ExitCode { get; private set; }

    public bool HasExited => IsDisposed || _exit.Task.IsCompleted;

    /// <summary>Whether the session has been disposed.</summary>
    public bool IsDisposed => DisposeCount > 0;

    /// <summary>How many times the session was killed while not yet disposed.</summary>
    public int KillCount { get; private set; }

    public string StandardError { get; private set; }

    public ValueTask DisposeAsync()
    {
        Kill();
        DisposeCount++;
        return ValueTask.CompletedTask;
    }

    /// <summary>Makes the running session exit on its own with <paramref name="exitCode"/>.</summary>
    public void Exit(int exitCode, string? standardError = null)
    {
        if (standardError is not null)
        {
            StandardError = standardError;
        }

        ExitCode ??= exitCode;
        _exit.TrySetResult(exitCode);
    }

    public void Kill()
    {
        if (IsDisposed)
        {
            return;
        }

        KillCount++;
        ExitCode ??= -1;
        _exit.TrySetResult(ExitCode.Value);
    }

    public Task<int> WaitForExitAsync(CancellationToken cancellationToken = default) => _exit.Task.WaitAsync(cancellationToken);
}
