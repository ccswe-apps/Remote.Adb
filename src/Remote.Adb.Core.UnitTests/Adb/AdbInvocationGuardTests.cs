using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace Remote.Adb.Core.UnitTests.Adb;

/// <summary>
/// Guards that adb is only ever run through <c>IAdbRunner</c>, so every invocation carries its environment (notably
/// <c>ADB_REJECT_KILL_SERVER=1</c>). Any other source file that reads <c>.AdbPath</c> is presumably launching adb itself.
/// </summary>
[SuppressMessage("ReSharper", "InconsistentNaming")]
public class AdbInvocationGuardTests
{
    private static readonly FrozenSet<string> AllowedFiles = new[]
    {
        "AdbRunner.cs",
        "AdbServerProcesses.cs",
        "AndroidSdk.cs",
        "IAndroidSdk.cs",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> BuildOutputDirectories = new[] { "bin", "obj" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static string? FindCoreProjectDirectory()
    {
        for (var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Remote.Adb.Core");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool IsBuildOutput(string coreDirectory, string file) =>
        Path.GetRelativePath(coreDirectory, file)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(BuildOutputDirectories.Contains);

    public class When_Core_Source_Is_Scanned : AdbInvocationGuardTests
    {
        [Test]
        public void It_reads_AdbPath_only_in_the_adb_runner_and_sdk_plumbing()
        {
            var coreDirectory = FindCoreProjectDirectory();
            if (coreDirectory is null)
            {
                Assert.Fail($"Couldn't find src{Path.DirectorySeparatorChar}Remote.Adb.Core above {TestContext.CurrentContext.TestDirectory}.");
                return;
            }

            var offenders = Directory
                .EnumerateFiles(coreDirectory, "*.cs", SearchOption.AllDirectories)
                .Where(file => !IsBuildOutput(coreDirectory, file))
                .Where(file => !AllowedFiles.Contains(Path.GetFileName(file)))
                .Where(file => File.ReadAllText(file).Contains(".AdbPath", StringComparison.Ordinal))
                .Select(file => Path.GetRelativePath(coreDirectory, file))
                .Order(StringComparer.Ordinal)
                .ToList();

            Assert.That(
                offenders,
                Is.Empty,
                $"These files read .AdbPath, so they presumably run adb directly: {string.Join(", ", offenders)}. Run adb through IAdbRunner instead, so every invocation carries its environment (ADB_REJECT_KILL_SERVER=1).");
        }
    }
}
