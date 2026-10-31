using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace KillMutants.EndToEnd.Tests;

/// <summary>
/// The notice a run writes about untracked files that appeared in the working tree while it was
/// going (#51), exercised through the real tool on the path review of #59 named: a red baseline,
/// which runs before any mutant is loaded and still leaves a file behind.
/// </summary>
[Collection(nameof(SerialEndToEnd))]
public class FilesLeftInTheTreeTests
{
    private const int CouldNotRun = 2;

    /// <summary>
    /// A test that writes beside its own source and fails, as a snapshot test does on a mismatch.
    /// Its failure makes the baseline red, so no mutant is ever loaded.
    /// </summary>
    private const string WritingTest =
        """
        using System.Runtime.CompilerServices;

        namespace Sample.Library.Tests;

        public class WritesBesideItsSource
        {
            [Fact]
            public void Leaves_a_received_file()
            {
                File.WriteAllText(Path.Combine(Here(), "Leftover.received.txt"), "received");
                Assert.Fail("Red on purpose: the baseline must not pass.");
            }

            private static string Here([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
        }
        """;

    [Fact]
    public async Task A_file_written_during_a_red_baseline_is_named_without_blaming_a_mutant()
    {
        using var fixture = FixtureCopy.Create();
        await File.WriteAllTextAsync(
            Path.Combine(fixture.Root, "Sample.Library.Tests", "WritesBesideItsSource.cs"),
            WritingTest,
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(fixture.Root, ".gitignore"), "bin/\nobj/\n", TestContext.Current.CancellationToken);
        await GitAsync(fixture.Root, "init", "-q");
        await GitAsync(fixture.Root, "add", "-A");
        await GitAsync(fixture.Root, "commit", "-q", "-m", "fixture");

        (int exitCode, string output, string error) = await RunAsync(fixture.Root);

        Assert.Equal(CouldNotRun, exitCode);
        Assert.Contains("does not pass against unmutated code", output, StringComparison.Ordinal);
        Assert.Contains("new untracked file(s) appeared", error, StringComparison.Ordinal);
        Assert.Contains("Sample.Library.Tests/Leftover.received.txt", error, StringComparison.Ordinal);
        Assert.Contains("cannot tell who wrote them", error, StringComparison.Ordinal);
        Assert.Contains("Look at each one before deleting it", error, StringComparison.Ordinal);
        Assert.DoesNotContain("while a mutant was loaded", error, StringComparison.Ordinal);
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(string directory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add(CommandLinePath());
        startInfo.ArgumentList.Add(directory);

        using var process = Process.Start(startInfo)!;

        Task<string> output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);

        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        return (process.ExitCode, await output, await error);
    }

    private static string CommandLinePath([CallerFilePath] string sourceFilePath = "")
    {
        string repositoryRoot = Path.GetFullPath(
            Path.Combine(Path.GetDirectoryName(sourceFilePath)!, "..", ".."));

        string[] segments = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)
            .Split(Path.DirectorySeparatorChar);

        return Path.Combine(
            repositoryRoot, "src", "KillMutants.Cli", "bin", segments[^2], segments[^1], "killmutants.dll");
    }

    private static async Task GitAsync(string root, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (string argument in (string[])["-c", "user.name=KillMutants", "-c", "user.email=tests@example.invalid", .. arguments])
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        string error = await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);

        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)} failed: {error}");
    }
}
