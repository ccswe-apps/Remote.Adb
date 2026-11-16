using Moq;
using Remote.Adb.Core.Common;

namespace Remote.Adb.Testing;

/// <summary>
/// Wraps a <see cref="Mock{T}"/> of <see cref="IProcessRunner"/> with helpers that hide its long parameter lists —
/// the process-runner counterpart of <see cref="AdbRunnerMock"/>.
/// </summary>
internal sealed class ProcessRunnerMock
{
    /// <summary>The underlying mock, for setups the helpers don't cover.</summary>
    public Mock<IProcessRunner> Inner { get; } = new();

    /// <summary>The mocked runner to hand to the class under test.</summary>
    public IProcessRunner Object => Inner.Object;

    /// <summary>Makes <c><paramref name="fileName"/> <paramref name="arguments"/></c> run to completion with the given output.</summary>
    public void Returns(string fileName, string[] arguments, string stdout, int exitCode = 0, string stderr = "") =>
        Inner
            .Setup(runner => runner.RunAsync(fileName, It.Is<IReadOnlyList<string>>(args => args.SequenceEqual(arguments)), It.IsAny<string?>(), It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessResult(exitCode, stdout, stderr));

    /// <summary>Makes every run-to-completion invocation return the given output.</summary>
    public void ReturnsForAny(string stdout = "", int exitCode = 0, string stderr = "") =>
        Inner
            .Setup(runner => runner.RunAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>(), It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessResult(exitCode, stdout, stderr));

    /// <summary>Makes every run-to-completion invocation hang until its token is cancelled (a stalled tool).</summary>
    public void StallsForAny() =>
        Inner
            .Setup(runner => runner.RunAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>(), It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .Returns(async (string _, IReadOnlyList<string> _, string? _, IReadOnlyDictionary<string, string>? _, CancellationToken token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new ProcessResult(0, string.Empty, string.Empty);
            });

    /// <summary>Makes every <see cref="IProcessRunner.StartSession"/> call return a session from <paramref name="factory"/>.</summary>
    public void StartsSession(Func<IProcessSession> factory) =>
        Inner
            .Setup(runner => runner.StartSession(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyDictionary<string, string>?>()))
            .Returns(factory);

    /// <summary>Makes successive <see cref="IProcessRunner.StartSession"/> calls return <paramref name="sessions"/> in order.</summary>
    public void StartsSessions(params IProcessSession[] sessions)
    {
        var sequence = Inner.SetupSequence(runner => runner.StartSession(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyDictionary<string, string>?>()));
        foreach (var session in sessions)
        {
            sequence = sequence.Returns(session);
        }
    }

    /// <summary>Verifies <c><paramref name="fileName"/></c> ran to completion with arguments matching <paramref name="match"/>, <paramref name="times"/>.</summary>
    public void VerifyRun(string fileName, Func<IReadOnlyList<string>, bool> match, Times times) =>
        Inner.Verify(runner => runner.RunAsync(fileName, It.Is<IReadOnlyList<string>>(args => match(args)), It.IsAny<string?>(), It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()), times);

    /// <summary>Verifies run-to-completion invocations happened <paramref name="times"/> in total, whatever the tool.</summary>
    public void VerifyRunAny(Times times) =>
        Inner.Verify(runner => runner.RunAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>(), It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()), times);

    /// <summary>Verifies <c><paramref name="fileName"/></c> was started as a session with arguments matching <paramref name="match"/>, <paramref name="times"/>.</summary>
    public void VerifySession(string fileName, Func<IReadOnlyList<string>, bool> match, Times times) =>
        Inner.Verify(runner => runner.StartSession(fileName, It.Is<IReadOnlyList<string>>(args => match(args)), It.IsAny<IReadOnlyDictionary<string, string>?>()), times);

    /// <summary>Verifies sessions were started <paramref name="times"/> in total, whatever the tool.</summary>
    public void VerifySessionAny(Times times) =>
        Inner.Verify(runner => runner.StartSession(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyDictionary<string, string>?>()), times);

    /// <summary>Verifies <c><paramref name="fileName"/> <paramref name="arguments"/></c> was launched fire-and-forget <paramref name="times"/>.</summary>
    public void VerifyStart(string fileName, string[] arguments, Times times) =>
        Inner.Verify(runner => runner.Start(fileName, It.Is<IReadOnlyList<string>>(args => args.SequenceEqual(arguments))), times);
}
