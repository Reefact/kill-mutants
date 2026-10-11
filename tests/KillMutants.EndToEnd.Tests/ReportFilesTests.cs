using KillMutants.Cli;

namespace KillMutants.EndToEnd.Tests;

/// <summary>
/// What is at the report's path when a run starts. A run that is killed never writes its report, so
/// whatever stood there before is what a step reading it would find - and an earlier run's finished
/// report, beside this run's partial log, would be published as this run's verdict (DEC0014).
/// </summary>
public sealed class ReportFilesTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("killmutants-test-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void An_earlier_runs_report_is_cleared_when_the_run_starts()
    {
        string report = Path.Combine(_directory, "report.json");

        File.WriteAllText(report, "{\"complete\":true}");

        using (Program.OpenVerdictLog(report))
        {
            Assert.False(File.Exists(report));
            Assert.True(File.Exists(Path.Combine(_directory, "report.jsonl")));
        }
    }

    /// <summary>
    /// Left for the report's own write to refuse at the end, with the message that says the measuring
    /// worked - deleting a directory a user put there is not this method's business.
    /// </summary>
    [Fact]
    public void A_directory_where_the_report_goes_is_left_alone()
    {
        string report = Path.Combine(_directory, "report.json");

        Directory.CreateDirectory(report);

        using (Program.OpenVerdictLog(report))
        {
            Assert.True(Directory.Exists(report));
        }
    }
}
