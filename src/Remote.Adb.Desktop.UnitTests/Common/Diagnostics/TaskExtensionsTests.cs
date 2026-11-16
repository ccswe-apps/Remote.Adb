using System.Diagnostics.CodeAnalysis;
using Remote.Adb.Desktop.Common.Diagnostics;
using Remote.Adb.Desktop.UnitTests.Fakes;

namespace Remote.Adb.Desktop.UnitTests.Common.Diagnostics;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class TaskExtensionsTests
{
    private protected RecordingExceptionReporter Reporter = null!;

    [SetUp]
    public void SetUp()
    {
        Reporter = new RecordingExceptionReporter();
    }

    public class When_Forget_Is_Called : TaskExtensionsTests
    {
        [Test]
        public void It_reports_a_task_that_faults_later()
        {
            var source = new TaskCompletionSource();
            source.Task.Forget(Reporter);

            source.SetException(new InvalidOperationException("boom"));

            Assert.That(Reporter.Reported.Single().Message, Is.EqualTo("boom"));
        }

        [Test]
        public void It_does_not_report_success_or_cancellation()
        {
            Task.CompletedTask.Forget(Reporter);
            Task.FromCanceled(new CancellationToken(canceled: true)).Forget(Reporter);

            Assert.That(Reporter.Reported, Is.Empty);
        }
    }
}
