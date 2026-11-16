using Remote.Adb.Desktop.Common.Diagnostics;

namespace Remote.Adb.Desktop.UnitTests.Fakes;

/// <summary>An <see cref="IUnhandledExceptionReporter"/> that records every exception reported to it, in order.</summary>
public sealed class RecordingExceptionReporter : IUnhandledExceptionReporter
{
    public List<Exception> Reported { get; } = [];

    public void Report(Exception exception) => Reported.Add(exception);
}
