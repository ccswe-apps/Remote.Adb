using System.Diagnostics.CodeAnalysis;
using Moq;
using Remote.Adb.Core.Common;
using Remote.Adb.Core.Diagnostics;

namespace Remote.Adb.Core.UnitTests.Diagnostics;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class SdkDiagnosticsTests
{
    private protected string TempDirectory = string.Empty;
    private protected Mock<IExecutableFinder> Finder = null!;

    [SetUp]
    public void SetUp()
    {
        TempDirectory = Path.Combine(Path.GetTempPath(), "remote-adb-diag-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempDirectory);

        // No java on PATH unless a test says otherwise.
        Finder = new Mock<IExecutableFinder>();
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(TempDirectory))
        {
            Directory.Delete(TempDirectory, true);
        }
    }

    private protected SdkDiagnostics CreateDiagnostics(SdkRootSource source = SdkRootSource.Override, string? sdkRoot = "/sdk", string? javaHome = null)
    {
        var sdk = new Mock<IAndroidSdk>();
        sdk.SetupGet(s => s.SdkRootSource).Returns(source);
        sdk.SetupGet(s => s.SdkRoot).Returns(sdkRoot);
        sdk.SetupGet(s => s.JavaHome).Returns(javaHome);

        return new SdkDiagnostics(sdk.Object, Finder.Object);
    }

    private protected string CreateFakeJdk()
    {
        var home = Path.Combine(TempDirectory, "jdk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(home, "bin"));
        File.WriteAllText(Path.Combine(home, "bin", ExecutableLocator.ExecutableName("java")), string.Empty);
        return home;
    }

    private protected void PutJavaOnPath() => Finder.Setup(finder => finder.FindOnPath("java")).Returns("/usr/bin/java");

    public class When_Evaluate_Is_Called : SdkDiagnosticsTests
    {
        [Test]
        public void It_reports_an_error_when_the_sdk_is_not_found()
        {
            var diagnostics = CreateDiagnostics(SdkRootSource.NotFound, sdkRoot: null, javaHome: CreateFakeJdk());

            var issue = diagnostics.Evaluate().Single();

            Assert.Multiple(() =>
            {
                Assert.That(issue.Title, Is.EqualTo("Android SDK"));
                Assert.That(issue.Severity, Is.EqualTo(DiagnosticSeverity.Error));
            });
        }

        [Test]
        public void It_reports_an_error_when_no_jdk_is_available()
        {
            var diagnostics = CreateDiagnostics(javaHome: null);

            var issue = diagnostics.Evaluate().Single();

            Assert.Multiple(() =>
            {
                Assert.That(issue.Title, Is.EqualTo("JDK"));
                Assert.That(issue.Severity, Is.EqualTo(DiagnosticSeverity.Error));
            });
        }

        [Test]
        public void It_reports_an_error_when_java_home_is_set_but_has_no_java()
        {
            var brokenJdk = Path.Combine(TempDirectory, "broken-jdk");
            Directory.CreateDirectory(brokenJdk);
            PutJavaOnPath();

            var diagnostics = CreateDiagnostics(javaHome: brokenJdk);

            var issue = diagnostics.Evaluate().Single();

            Assert.Multiple(() =>
            {
                Assert.That(issue.Title, Is.EqualTo("JDK"));
                Assert.That(issue.Severity, Is.EqualTo(DiagnosticSeverity.Error));
            });
        }

        [Test]
        public void It_reports_nothing_when_the_sdk_and_jdk_are_explicit()
        {
            var diagnostics = CreateDiagnostics(SdkRootSource.EnvironmentVariable, javaHome: CreateFakeJdk());

            Assert.That(diagnostics.Evaluate(), Is.Empty);
        }

        [Test]
        public void It_warns_when_java_is_only_on_the_path()
        {
            PutJavaOnPath();
            var diagnostics = CreateDiagnostics(javaHome: null);

            var issue = diagnostics.Evaluate().Single();

            Assert.Multiple(() =>
            {
                Assert.That(issue.Title, Is.EqualTo("JDK"));
                Assert.That(issue.Severity, Is.EqualTo(DiagnosticSeverity.Warning));
                Assert.That(issue.Message, Does.Contain("/usr/bin/java"));
            });
        }

        [Test]
        public void It_warns_when_the_sdk_is_only_the_default_guess()
        {
            var diagnostics = CreateDiagnostics(SdkRootSource.DefaultFallback, javaHome: CreateFakeJdk());

            var issue = diagnostics.Evaluate().Single();

            Assert.Multiple(() =>
            {
                Assert.That(issue.Title, Is.EqualTo("Android SDK"));
                Assert.That(issue.Severity, Is.EqualTo(DiagnosticSeverity.Warning));
            });
        }
    }
}
