using Microsoft.Extensions.Time.Testing;

namespace Remote.Adb.Testing;

/// <summary>Drives code under test that waits on a <see cref="FakeTimeProvider"/>.</summary>
internal static class FakeTimeProviderExtensions
{
    private static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(10);

    /// <summary>
    /// Advances <paramref name="time"/> by <paramref name="step"/> until <paramref name="task"/> completes, then returns
    /// its result — for an operation that polls, delays or times out on the fake clock. Fails after
    /// <paramref name="maxSteps"/> steps rather than spinning forever.
    /// </summary>
    /// <inheritdoc cref="AdvanceUntilCompleteAsync{T}"/>
    public static async Task AdvanceUntilCompleteAsync(this FakeTimeProvider time, Task task, TimeSpan step, int maxSteps = 1000) =>
        await time.AdvanceUntilCompleteAsync(Complete(task), step, maxSteps);

    public static async Task<T> AdvanceUntilCompleteAsync<T>(this FakeTimeProvider time, Task<T> task, TimeSpan step, int maxSteps = 1000)
    {
        for (var steps = 0; !task.IsCompleted; steps++)
        {
            if (steps >= maxSteps)
            {
                throw new TimeoutException($"The operation didn't complete within {maxSteps} steps of {step} on the fake clock.");
            }

            time.Advance(step);

            // Firing a fake timer completes work on thread-pool threads (a cancelled delay, an awaited mock). Give those
            // continuations a brief real-time window to run — and register their next timer — before stepping again;
            // a bare Task.Yield isn't enough on a busy machine, where the loop can outrun them.
            await Task.WhenAny(task, Task.Delay(SettleTime));
        }

        return await task;
    }

    private static async Task<bool> Complete(Task task)
    {
        await task;
        return true;
    }
}
