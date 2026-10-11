using KillMutants.Cli;

namespace KillMutants.EndToEnd.Tests;

/// <summary>
/// A series lives in the plan and the log beside the report, and measures the whole codebase at one
/// commit (DEC0015); the settings refuse what would contradict either before anything is built.
/// </summary>
public class ResumeSettingsTests
{
    private static CommandLineOptions Asked(params string[] arguments) =>
        CommandLineOptions.Parse(["/work", .. arguments]);

    [Fact]
    public void The_series_name_reaches_the_settings()
    {
        RunSettings settings = RunSettings.From(
            Asked("--resume", "0123abcd", "--report-json", "/work/out/report.json"), file: null);

        Assert.Equal("0123abcd", settings.Resume);
    }

    [Fact]
    public void Without_a_series_a_run_names_none()
    {
        Assert.Null(RunSettings.From(Asked("--report-json", "/work/out/report.json"), file: null).Resume);
    }

    [Theory]
    [InlineData("--resume")]
    [InlineData("--resume", "--report-json", "/work/report.json")]
    [InlineData("--resume", " ")]
    public void A_series_needs_a_name(params string[] arguments)
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(() => Asked(arguments));

        Assert.Contains("name", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_series_needs_a_report_to_live_beside()
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(
            () => RunSettings.From(Asked("--resume", "0123abcd"), file: null));

        Assert.Contains("--report-json", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_file_can_supply_the_report_a_series_needs()
    {
        var file = new ConfigurationFile(ReportJson: "artifacts/mutation.json") { Directory = "/work" };

        Assert.Equal("0123abcd", RunSettings.From(Asked("--resume", "0123abcd"), file).Resume);
    }

    [Fact]
    public void A_series_cannot_be_combined_with_since()
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(
            () => RunSettings.From(
                Asked("--resume", "0123abcd", "--since", "main", "--report-json", "/work/r.json"),
                file: null));

        Assert.Contains("--since", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_report_named_like_its_plan_is_refused()
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(
            () => RunSettings.From(Asked("--report-json", "/work/report.plan.json"), file: null));

        Assert.Contains(".plan.json", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("'--report-json'", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_report_named_like_its_plan_in_the_file_is_refused_and_named()
    {
        var file = new ConfigurationFile(ReportJson: "artifacts/mutation.plan.json") { Directory = "/work" };

        ArgumentException refusal = Assert.Throws<ArgumentException>(() => RunSettings.From(Asked(), file));

        Assert.Contains("'reportJson' in killmutants.json", refusal.Message, StringComparison.Ordinal);
    }
}
