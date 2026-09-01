using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace KillMutants.EndToEnd.Tests;

/// <summary>
/// The exit codes are a public contract: CI jobs and scripts encode them, so they are exercised
/// through the real command-line tool rather than through the policy behind it.
/// </summary>
[Collection(nameof(SerialEndToEnd))]
public class CommandLineTests
{
    private const int Success = 0;
    private const int ScoreBelowThreshold = 1;
    private const int CouldNotRun = 2;
    private const int BadUsage = 64;

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add(CommandLinePath());

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;

        Task<string> output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);

        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        return (process.ExitCode, await output, await error);
    }

    /// <summary>
    /// The built tool, found from this file's path and the configuration this test was built in, so
    /// it works the same from Debug and Release.
    /// </summary>
    private static string CommandLinePath([CallerFilePath] string sourceFilePath = "")
    {
        string repositoryRoot = Path.GetFullPath(
            Path.Combine(Path.GetDirectoryName(sourceFilePath)!, "..", ".."));

        // <root>/tests/KillMutants.EndToEnd.Tests/bin/<configuration>/<tfm>/
        string[] segments = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)
            .Split(Path.DirectorySeparatorChar);
        string targetFramework = segments[^1];
        string configuration = segments[^2];

        return Path.Combine(
            repositoryRoot, "src", "KillMutants.Cli", "bin", configuration, targetFramework,
            "killmutants.dll");
    }

    [Fact]
    public async Task A_run_that_meets_its_threshold_succeeds()
    {
        using var fixture = FixtureCopy.Create();

        (int exitCode, string output, _) = await RunAsync(fixture.Root, "--break-at", "100");

        Assert.Equal(Success, exitCode);
        Assert.Contains("Mutation score: 100%", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_run_with_no_threshold_succeeds_whatever_the_score()
    {
        using var fixture = FixtureCopy.Create();
        fixture.AddCodeNoTestReaches();

        (int exitCode, _, _) = await RunAsync(fixture.Root);

        // Without --break-at the tool only reports. Failing a build on a score nobody asked about
        // would make adopting it a breaking change.
        Assert.Equal(Success, exitCode);
    }

    /// <summary>
    /// The quality gate must react to code that has no tests at all, which is the harshest thing a
    /// mutation run can find. Before uncovered mutants were counted as undetected this returned
    /// success: the untested code was excluded from the denominator, so the score stayed at 100%.
    /// </summary>
    [Fact]
    public async Task Code_with_no_tests_at_all_fails_the_threshold()
    {
        using var fixture = FixtureCopy.Create();
        fixture.AddCodeNoTestReaches();

        (int exitCode, string output, string message) = await RunAsync(fixture.Root, "--break-at", "100");

        Assert.Equal(ScoreBelowThreshold, exitCode);
        Assert.Contains("below the 100% threshold", message, StringComparison.Ordinal);
        Assert.Contains("No coverage:", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Excluding every project is a mistake worth its own message: "no project found" would send
    /// the user looking at the directory they gave rather than at the pattern they wrote.
    /// </summary>
    [Fact]
    public async Task Excluding_everything_says_so_rather_than_reporting_an_empty_directory()
    {
        using var fixture = FixtureCopy.Create();

        (int exitCode, _, string error) = await RunAsync(fixture.Root, "-e", "*");

        Assert.Equal(CouldNotRun, exitCode);
        Assert.Contains("was excluded", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_score_below_the_threshold_is_distinguishable_from_a_broken_run()
    {
        using var fixture = FixtureCopy.Create();
        fixture.AddPartlyTestedCode();

        (int belowThreshold, _, string thresholdMessage) =
            await RunAsync(fixture.Root, "--break-at", "100");
        (int brokenRun, _, string brokenMessage) = await RunAsync("/killmutants-no-such-directory");

        // The distinction that earns its keep: "your tests are weaker than you asked for" and
        // "this tool did not work" call for different reactions from a CI job.
        Assert.Equal(ScoreBelowThreshold, belowThreshold);
        Assert.Equal(CouldNotRun, brokenRun);
        Assert.Contains("below the 100% threshold", thresholdMessage, StringComparison.Ordinal);
        Assert.Contains("is not a directory", brokenMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--nonsense")]
    [InlineData("--break-at")]
    [InlineData("--break-at", "150")]
    [InlineData("--break-at", "-1")]
    [InlineData("--break-at", "not-a-number")]
    [InlineData("--parallel", "0")]
    public async Task A_command_line_that_cannot_be_understood_is_its_own_outcome(params string[] arguments)
    {
        (int exitCode, _, _) = await RunAsync(arguments);

        Assert.Equal(BadUsage, exitCode);
    }

    [Fact]
    public async Task Help_is_a_success_and_documents_the_exit_codes()
    {
        (int exitCode, string output, _) = await RunAsync("--help");

        Assert.Equal(Success, exitCode);
        Assert.Contains("Exit codes:", output, StringComparison.Ordinal);
        Assert.Contains("--break-at", output, StringComparison.Ordinal);
    }
}
