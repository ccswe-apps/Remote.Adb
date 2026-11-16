namespace Remote.Adb.Core.Common;

/// <summary>A held <see cref="SemaphoreSlim"/>, from <see cref="SemaphoreSlimExtensions.LockAsync"/>; disposing releases it.</summary>
internal readonly struct SemaphoreLease : IDisposable
{
    private readonly SemaphoreSlim _semaphore;

    /// <summary>Wraps an already-entered <paramref name="semaphore"/>.</summary>
    /// <param name="semaphore">The semaphore the caller holds.</param>
    public SemaphoreLease(SemaphoreSlim semaphore)
    {
        _semaphore = semaphore;
    }

    /// <summary>Releases the semaphore.</summary>
    public void Dispose() => _semaphore.Release();
}
