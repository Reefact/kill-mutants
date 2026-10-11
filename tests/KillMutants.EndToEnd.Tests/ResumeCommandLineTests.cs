using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace KillMutants.EndToEnd.Tests;

/// <summary>
/// What the command line makes of a series: the code state read from git, the log continued or
/// emptied, the refusals that keep a series honest (DEC0015). Exercised through the real tool,
/// because the exit codes are its contract.
/// </summary>
[Collection(nameof(SerialEndToEnd))]
public class ResumeCommandLineTests
{
    private const int Success = 0;
    private const int CouldNotRun = 2;

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(params string[] arguments)
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

    private static string CommandLinePath([CallerFilePath] string sourceFilePath = "")
    {
        string repositoryRoot = Path.GetFullPath(
            Path.Combine(Path.GetDirectoryName(sourceFilePath)!, "..", ".."));

        string[] segments = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)
            .Split(Path.DirectorySeparatorChar);
        string targetFramework = segments[^1];
        string configuration = segments[^2];

        return Path.Combine(
            repositoryRoot, "src", "KillMutants.Cli", "bin", configuration, targetFramework, "killmutants.dll");
    }

    /// <summary>Runs git in the fixture, as a person setting up a working copy would.</summary>
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

    /// <summary>
    /// Turns the copied fixture into a committed working copy. Build output is ignored, as it is in
    /// any real repository; the tool's own files beside the report need no ignoring.
    /// </summary>
    private static async Task MakeWorkingCopyAsync(FixtureCopy fixture)
    {
        await File.WriteAllTextAsync(
            Path.Combine(fixture.Root, ".gitignore"), "bin/\nobj/\n", TestContext.Current.CancellationToken);
        await GitAsync(fixture.Root, "init", "-q");
        await GitAsync(fixture.Root, "add", "-A");
        await GitAsync(fixture.Root, "commit", "-q", "-m", "fixture");
    }

    private static async Task<JsonDocument> ReadAsync(string path) =>
        JsonDocument.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));

    [Fact]
    public async Task A_series_is_continued_on_the_same_commit_and_refused_once_the_code_moved()
    {
        using var fixture = FixtureCopy.Create();
        await MakeWorkingCopyAsync(fixture);

        string reportPath = Path.Combine(fixture.Root, "out", "report.json");
        string logPath = Path.ChangeExtension(reportPath, ".jsonl");
        string planPath = Path.ChangeExtension(reportPath, ".plan.json");

        (int exitCode, _, string error) = await RunAsync(
            fixture.Root, "--mutators", "Comparison", "--report-json", reportPath, "--resume", "one");

        Assert.True(exitCode == Success, error);

        int selected;

        using (JsonDocument first = await ReadAsync(reportPath))
        {
            JsonElement series = first.RootElement.GetProperty("series");

            Assert.True(first.RootElement.GetProperty("complete").GetBoolean());
            Assert.Equal("one", series.GetProperty("id").GetString());
            Assert.Equal(0, series.GetProperty("inherited").GetInt32());
            Assert.Equal(1, series.GetProperty("runs").GetArrayLength());

            selected = first.RootElement.GetProperty("totals").GetProperty("mutants").GetInt32();
        }

        Assert.True(selected > 0);
        Assert.True(File.Exists(planPath));

        // The same series, the same commit: everything is inherited and the log is not re-told.
        (exitCode, _, error) = await RunAsync(
            fixture.Root, "--mutators", "Comparison", "--report-json", reportPath, "--resume", "one");

        Assert.True(exitCode == Success, error);

        using (JsonDocument second = await ReadAsync(reportPath))
        {
            JsonElement series = second.RootElement.GetProperty("series");

            Assert.True(second.RootElement.GetProperty("complete").GetBoolean());
            Assert.Equal(selected, series.GetProperty("inherited").GetInt32());
            Assert.Equal(2, series.GetProperty("runs").GetArrayLength());
            Assert.NotEqual(JsonValueKind.Null, second.RootElement.GetProperty("score").ValueKind);
        }

        Assert.Equal(selected, (await File.ReadAllLinesAsync(logPath, TestContext.Current.CancellationToken)).Length);

        // Another name starts another series, from nothing.
        (exitCode, _, error) = await RunAsync(
            fixture.Root, "--mutators", "Comparison", "--report-json", reportPath, "--resume", "two");

        Assert.True(exitCode == Success, error);

        using (JsonDocument third = await ReadAsync(reportPath))
        {
            JsonElement series = third.RootElement.GetProperty("series");

            Assert.Equal("two", series.GetProperty("id").GetString());
            Assert.Equal(0, series.GetProperty("inherited").GetInt32());
            Assert.Equal(1, series.GetProperty("runs").GetArrayLength());
        }

        Assert.Equal(selected, (await File.ReadAllLinesAsync(logPath, TestContext.Current.CancellationToken)).Length);

        // The code moves, and the series of that name may not be continued on it.
        string planBefore = await File.ReadAllTextAsync(planPath, TestContext.Current.CancellationToken);

        await File.AppendAllTextAsync(
            Path.Combine(fixture.Root, "Sample.Library", "Ages.cs"), "\n// moved\n",
            TestContext.Current.CancellationToken);
        await GitAsync(fixture.Root, "add", "-A");
        await GitAsync(fixture.Root, "commit", "-q", "-m", "moved");

        (exitCode, _, error) = await RunAsync(
            fixture.Root, "--mutators", "Comparison", "--report-json", reportPath, "--resume", "two");

        Assert.Equal(CouldNotRun, exitCode);
        Assert.Contains("cannot be continued", error, StringComparison.Ordinal);
        Assert.Contains("Nothing was changed", error, StringComparison.Ordinal);
        Assert.Equal(planBefore, await File.ReadAllTextAsync(planPath, TestContext.Current.CancellationToken));
        Assert.Equal(selected, (await File.ReadAllLinesAsync(logPath, TestContext.Current.CancellationToken)).Length);
    }

    [Fact]
    public async Task A_series_cannot_start_outside_a_git_working_copy()
    {
        using var fixture = FixtureCopy.Create();
        string reportPath = Path.Combine(fixture.Root, "out", "report.json");

        (int exitCode, _, string error) = await RunAsync(
            fixture.Root, "--report-json", reportPath, "--resume", "one");

        Assert.Equal(CouldNotRun, exitCode);
        Assert.Contains("git working copy", error, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.ChangeExtension(reportPath, ".plan.json")));
    }

    [Fact]
    public async Task A_series_started_on_uncommitted_changes_is_told_it_cannot_be_continued()
    {
        using var fixture = FixtureCopy.Create();
        await MakeWorkingCopyAsync(fixture);
        await File.AppendAllTextAsync(
            Path.Combine(fixture.Root, "Sample.Library", "Ages.cs"), "\n// not committed\n",
            TestContext.Current.CancellationToken);

        string reportPath = Path.Combine(fixture.Root, "out", "report.json");

        (int exitCode, _, string error) = await RunAsync(
            fixture.Root, "--mutators", "Comparison", "--report-json", reportPath, "--resume", "one");

        Assert.True(exitCode == Success, error);
        Assert.Contains("uncommitted changes", error, StringComparison.Ordinal);

        using JsonDocument plan = await ReadAsync(Path.ChangeExtension(reportPath, ".plan.json"));

        Assert.False(plan.RootElement.GetProperty("identity").GetProperty("code").GetProperty("isExact").GetBoolean());
    }
}
