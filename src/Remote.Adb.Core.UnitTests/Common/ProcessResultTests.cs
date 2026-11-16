using System.Diagnostics.CodeAnalysis;
using Remote.Adb.Core.Common;

namespace Remote.Adb.Core.UnitTests.Common;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class ProcessResultTests
{
    public class When_Describe_Is_Called : ProcessResultTests
    {
        [Test]
        public void It_prefers_stderr()
        {
            var result = new ProcessResult(1, "stdout text\n", "  error: no devices\n");

            Assert.That(result.Describe("adb"), Is.EqualTo("error: no devices"));
        }

        [Test]
        public void It_falls_back_to_stdout()
        {
            var result = new ProcessResult(1, "failed to connect\r\n", "");

            Assert.That(result.Describe("adb"), Is.EqualTo("failed to connect"));
        }

        [Test]
        public void It_reports_the_exit_code_when_the_tool_printed_nothing()
        {
            var result = new ProcessResult(255, "", "   ");

            Assert.That(result.Describe("ssh"), Is.EqualTo("ssh exited with code 255"));
        }
    }
}
