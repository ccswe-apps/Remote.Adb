using Microsoft.Extensions.Time.Testing;

namespace Remote.Adb.Testing;

/// <summary>Drives code under test that waits on a <see cref="FakeTimeProvider"/>.</summary>
internal static class FakeTimeProviderExtensions
{
    /// <summary>
    /// Advances <paramref name="time"/> by <paramref name="step"/> until <paramref name="task"/> completes, then returns
    /// its result — for an operation that polls, delays or times out on the fake clock. Fails after
    /// <paramref name="maxSteps"/> steps rather than spinning forever.
    /// </summary>
    public static async Task<T> AdvanceUntilCompleteAsync<T>(this FakeTimeProvider time, Task<T> task, TimeSpan step, int maxSteps = 1000)
    {
        for (var steps = 0; !task.IsCompleted; steps++)
        {
            if (steps >= maxSteps)
            {
                throw new TimeoutException($"The operation didn't complete within {maxSteps} steps of {step} on the fake clock.");
            }

            // Let continuations scheduled by the previous step register their next timer before advancing again.
            await Task.Yield();
            time.Advance(step);
        }

        return await task;
    }
}
