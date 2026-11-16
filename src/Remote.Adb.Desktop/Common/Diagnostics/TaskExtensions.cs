namespace Remote.Adb.Desktop.Common.Diagnostics;

/// <summary>Fire-and-forget that doesn't lose failures.</summary>
public static class TaskExtensions
{
    /// <summary>
    /// Lets <paramref name="task"/> run without awaiting it, routing any exception it ends with to
    /// <paramref name="reporter"/> instead of dropping it. Cancellation is not a failure and is ignored.
    /// </summary>
    public static void Forget(this Task task, IUnhandledExceptionReporter reporter) => _ = ObserveAsync(task, reporter);

    private static async Task ObserveAsync(Task task, IUnhandledExceptionReporter reporter)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            // Cancelled work isn't a failure.
        }
        catch (Exception exception)
        {
            reporter.Report(exception);
        }
    }
}
