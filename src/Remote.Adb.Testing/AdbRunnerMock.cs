using Moq;
using Remote.Adb.Core.Adb;
using Remote.Adb.Core.Common;

namespace Remote.Adb.Testing;

/// <summary>
/// Wraps a <see cref="Mock{T}"/> of <see cref="IAdbRunner"/> with helpers that match adb invocations by their arguments.
/// </summary>
internal sealed class AdbRunnerMock
{
    /// <summary>The underlying mock, for setups the helpers don't cover.</summary>
    public Mock<IAdbRunner> Inner { get; } = new();

    /// <summary>The mocked runner to hand to the class under test.</summary>
    public IAdbRunner Object => Inner.Object;

    /// <summary>Makes <c>adb <paramref name="arguments"/></c> return the given output.</summary>
    public void Returns(string[] arguments, string stdout, int exitCode = 0, string stderr = "") =>
        Inner
            .Setup(runner => runner.RunAsync(It.Is<IReadOnlyList<string>>(args => args.SequenceEqual(arguments)), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessResult(exitCode, stdout, stderr));

    /// <summary>Makes every adb invocation return the given output.</summary>
    public void ReturnsForAny(string stdout, int exitCode = 0, string stderr = "") =>
        Inner
            .Setup(runner => runner.RunAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessResult(exitCode, stdout, stderr));

    /// <summary>Makes every adb invocation throw <paramref name="exception"/>.</summary>
    public void ThrowsForAny(Exception exception) =>
        Inner
            .Setup(runner => runner.RunAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

    /// <summary>Verifies <c>adb <paramref name="arguments"/></c> ran <paramref name="times"/>.</summary>
    public void Verify(string[] arguments, Times times) =>
        Inner.Verify(runner => runner.RunAsync(It.Is<IReadOnlyList<string>>(args => args.SequenceEqual(arguments)), It.IsAny<string?>(), It.IsAny<CancellationToken>()), times);

    /// <summary>Verifies adb ran <paramref name="times"/> in total, whatever the arguments.</summary>
    public void VerifyAny(Times times) =>
        Inner.Verify(runner => runner.RunAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), times);

    /// <summary>Verifies no adb invocation was made beyond those already verified.</summary>
    public void VerifyNoOtherCalls() => Inner.VerifyNoOtherCalls();

    /// <summary>Verifies <c>adb <paramref name="arguments"/></c> ran with <paramref name="stdin"/> written to its standard input.</summary>
    public void VerifyStdin(string[] arguments, string stdin) =>
        Inner.Verify(runner => runner.RunAsync(It.Is<IReadOnlyList<string>>(args => args.SequenceEqual(arguments)), stdin, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
}
