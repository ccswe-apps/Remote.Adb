namespace Remote.Adb.Core.Common;

/// <summary>
/// The shared "run this, but give up after a while" helper: every bounded external call (adb connect / pair, a server
/// kill, an ssh round-trip) goes through it instead of hand-rolling a linked token, <c>CancelAfter</c>, and an
/// <see cref="OperationCanceledException"/> filter.
/// </summary>
internal static class Timeouts
{
    /// <summary>
    /// Runs <paramref name="operation"/> with a token that cancels after <paramref name="timeout"/>. Returns
    /// <c>(true, value)</c> when it finished in time and <c>(false, default)</c> when the timeout fired. Cancellation of
    /// <paramref name="cancellationToken"/> by the caller still propagates as <see cref="OperationCanceledException"/>.
    /// </summary>
    /// <param name="operation">The work to run; it must observe the token it's given.</param>
    /// <param name="timeout">How long to allow.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <param name="timeProvider">The clock (tests pass a fake); defaults to <see cref="TimeProvider.System"/>.</param>
    public static async Task<(bool Completed, T? Value)> TryWithTimeoutAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        TimeProvider? timeProvider = null)
    {
        using var timeoutSource = new CancellationTokenSource(timeout, timeProvider ?? TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        try
        {
            return (true, await operation(linked.Token));
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return (false, default);
        }
    }
}
