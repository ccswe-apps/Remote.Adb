using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace Remote.Adb.Core.UnitTests.Adb;

/// <summary>
/// Guards that adb is only ever run through <c>IAdbRunner</c>, so every invocation carries its environment (notably
/// <c>ADB_REJECT_KILL_SERVER=1</c>). Any other source file — in Core or either front-end — that reads <c>.AdbPath</c>
/// is presumably launching adb itself.
/// </summary>
[SuppressMessage("ReSharper", "InconsistentNaming")]
public class AdbInvocationGuardTests
{
    // Relative to src/, with '/' separators — a bare file name would also excuse a same-named file elsewhere.
    private static readonly FrozenSet<string> AllowedFiles = new[]
    {
        "Remote.Adb.Core/Adb/AdbRunner.cs",
        "Remote.Adb.Core/Common/AndroidSdk.cs",
        "Remote.Adb.Core/Common/IAndroidSdk.cs",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> BuildOutputDirectories = new[] { "bin", "obj" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] ScannedProjects = ["Remote.Adb.Core", "Remote.Adb.Desktop", "Remote.Adb.Console"];

    private static string? FindSourceDirectory()
    {
        for (var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src");
            if (Directory.Exists(Path.Combine(candidate, "Remote.Adb.Core")))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool IsBuildOutput(string relativePath) =>
        relativePath.Split('/').Any(BuildOutputDirectories.Contains);

    private static string RelativePath(string sourceDirectory, string file) =>
        Path.GetRelativePath(sourceDirectory, file).Replace(Path.DirectorySeparatorChar, '/');

    public class When_Source_Is_Scanned : AdbInvocationGuardTests
    {
        [Test]
        public void It_reads_AdbPath_only_in_the_adb_runner_and_sdk_plumbing()
        {
            var sourceDirectory = FindSourceDirectory();
            if (sourceDirectory is null)
            {
                Assert.Fail($"Couldn't find src{Path.DirectorySeparatorChar}Remote.Adb.Core above {TestContext.CurrentContext.TestDirectory}.");
                return;
            }

            var offenders = ScannedProjects
                .Select(project => Path.Combine(sourceDirectory, project))
                .Where(Directory.Exists)
                .SelectMany(project => Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories))
                .Select(file => (File: file, Relative: RelativePath(sourceDirectory, file)))
                .Where(entry => !IsBuildOutput(entry.Relative) && !AllowedFiles.Contains(entry.Relative))
                .Where(entry => File.ReadAllText(entry.File).Contains(".AdbPath", StringComparison.Ordinal))
                .Select(entry => entry.Relative)
                .Order(StringComparer.Ordinal)
                .ToList();

            Assert.That(
                offenders,
                Is.Empty,
                $"These files read .AdbPath, so they presumably run adb directly: {string.Join(", ", offenders)}. Run adb through IAdbRunner instead, so every invocation carries its environment (ADB_REJECT_KILL_SERVER=1).");
        }
    }
}
