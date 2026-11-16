namespace Remote.Adb.Core.Common;

/// <summary>
/// <c>using var lease = await gate.LockAsync()</c> in place of the wait / try / finally-release pattern, so a
/// semaphore used as an async mutex can't be left held by a forgotten <c>Release</c>.
/// </summary>
internal static class SemaphoreSlimExtensions
{
    /// <summary>
    /// Waits for <paramref name="semaphore"/> and returns a lease that releases it when disposed. If the wait is
    /// cancelled it throws <see cref="OperationCanceledException"/> and nothing is held.
    /// </summary>
    /// <param name="semaphore">The semaphore to enter.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    public static async Task<SemaphoreLease> LockAsync(this SemaphoreSlim semaphore, CancellationToken cancellationToken = default)
    {
        await semaphore.WaitAsync(cancellationToken);
        return new SemaphoreLease(semaphore);
    }
}
