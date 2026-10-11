using KillMutants.Cli;
using KillMutants.Execution;
using KillMutants.Reporting;

namespace KillMutants.EndToEnd.Tests;

/// <summary>
/// What is at the report's path when a run starts. A run that is killed never writes its report, so
/// whatever stood there before is what a step reading it would find - and an earlier run's finished
/// report, beside this run's partial log, would be published as this run's verdict (DEC0014). A run
/// that names no series also replaces the plan an earlier run left (DEC0016).
/// </summary>
public sealed class ReportFilesTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("killmutants-test-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>A run that names no series, pointed at a directory outside any working copy.</summary>
    private RunSettings Settings(string report) =>
        new(_directory, "Release", null, true, [], [], [], 0, report, null, null);

    [Fact]
    public async Task An_earlier_runs_report_is_cleared_when_the_run_starts()
    {
        string report = Path.Combine(_directory, "report.json");

        File.WriteAllText(report, "{\"complete\":true}");

        Program.ReportFiles files = await Program.OpenReportFilesAsync(Settings(report), report);

        using (files.Log)
        {
            Assert.False(File.Exists(report));
            Assert.True(File.Exists(Path.Combine(_directory, "report.jsonl")));
        }
    }

    [Fact]
    public async Task An_earlier_runs_plan_is_cleared_by_a_run_that_names_no_series()
    {
        string report = Path.Combine(_directory, "report.json");
        string plan = Path.Combine(_directory, "report.plan.json");

        File.WriteAllText(plan, "not even a plan, and never read");
        File.WriteAllText(plan + ".tmp", "{ half");

        Program.ReportFiles files = await Program.OpenReportFilesAsync(Settings(report), report);

        using (files.Log)
        {
            Assert.False(File.Exists(plan));
            Assert.False(File.Exists(plan + ".tmp"));
            Assert.Null(files.Continuation.Series);
            Assert.Null(files.Continuation.Earlier);
            Assert.Null(files.Continuation.Identity.Code);
        }
    }

    [Fact]
    public async Task A_run_that_starts_a_series_keeps_no_report_to_put_back()
    {
        string report = Path.Combine(_directory, "report.json");

        File.WriteAllText(report, "{\"complete\":true}");

        Program.ReportFiles files = await Program.OpenReportFilesAsync(Settings(report), report);

        using (files.Log)
        {
            Assert.Null(files.EarlierReport);

            files.RestoreEarlierReport();

            Assert.False(File.Exists(report));
        }
    }

    /// <summary>
    /// A run that continues a series clears the report like any other, and may yet be refused once
    /// its mutants are generated; the refusal promises the files as they were, so the report comes back
    /// byte for byte.
    /// </summary>
    [Fact]
    public void The_report_a_continuing_run_cleared_is_put_back_on_a_late_refusal()
    {
        string report = Path.Combine(_directory, "report.json");
        var store = new RunPlanStore(report);
        var identity = new RunIdentity(new CodeState("0123abcd", IsExact: true), "test-tool", "Release", false, [], [], []);
        byte[] earlier = "{\"complete\":true}"u8.ToArray();

        var files = new Program.ReportFiles(
            VerdictLog.Create(store.LogPath),
            new RunContinuation("one", identity, Earlier: null, [], store),
            earlier);

        using (files.Log)
        {
            files.RestoreEarlierReport();
        }

        Assert.Equal(earlier, File.ReadAllBytes(report));
    }

    /// <summary>
    /// Left for the report's own write to refuse at the end, with the message that says the measuring
    /// worked - deleting a directory a user put there is not this method's business.
    /// </summary>
    [Fact]
    public async Task A_directory_where_the_report_goes_is_left_alone()
    {
        string report = Path.Combine(_directory, "report.json");

        Directory.CreateDirectory(report);

        Program.ReportFiles files = await Program.OpenReportFilesAsync(Settings(report), report);

        using (files.Log)
        {
            Assert.True(Directory.Exists(report));
        }
    }
}
