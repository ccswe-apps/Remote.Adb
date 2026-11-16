namespace Remote.Adb.Desktop.Common.Diagnostics;

/// <summary>
/// The last line of defence for an exception nothing else handled — a faulted fire-and-forget task, or a command that
/// threw on the UI thread. Reporting it logs the failure and tells the user, so it is never silent and never crashes
/// the app.
/// </summary>
public interface IUnhandledExceptionReporter
{
    /// <summary>Logs <paramref name="exception"/> and shows an error toast. Safe to call from any thread.</summary>
    void Report(Exception exception);
}
