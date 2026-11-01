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

    /// <summary>DEC0025: a series names the untracked files its tests write, and only a series does.</summary>
    [Fact]
    public void The_untracked_files_a_series_ignores_reach_the_settings()
    {
        RunSettings settings = RunSettings.From(
            Asked(
                "--resume", "0123abcd", "--report-json", "/work/out/report.json",
                "--ignore-untracked", "*.received.html", "--ignore-untracked", "*.received.json"),
            file: null);

        Assert.Equal(["*.received.html", "*.received.json"], settings.IgnoreUntracked);
    }

    [Fact]
    public void Ignoring_untracked_files_without_a_series_is_refused()
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(() => RunSettings.From(
            Asked("--report-json", "/work/out/report.json", "--ignore-untracked", "*.received.html"),
            file: null));

        Assert.Contains("'--ignore-untracked' needs '--resume'", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ignoring_untracked_files_needs_a_pattern()
    {
        Assert.Throws<ArgumentException>(() => Asked("--resume", "0123abcd", "--ignore-untracked"));
    }

    [Theory]
    [InlineData("*", "every untracked file")]
    [InlineData("*.received.*", "'*.cs'")]
    [InlineData("*.json", "'global.json'")]
    public void A_pattern_that_could_cover_code_is_refused_before_anything_runs(string pattern, string said)
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(() => RunSettings.From(
            Asked("--resume", "0123abcd", "--report-json", "/work/out/report.json", "--ignore-untracked", pattern),
            file: null));

        Assert.Contains(said, refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>A habit of the project, which waits for the run it applies to.</summary>
    [Fact]
    public void The_file_key_applies_to_a_series_and_waits_on_any_other_run()
    {
        var file = new ConfigurationFile(IgnoreUntracked: ["*.received.html"]) { Directory = "/work" };

        Assert.Equal(
            ["*.received.html"],
            RunSettings.From(Asked("--resume", "0123abcd", "--report-json", "/work/r.json"), file).IgnoreUntracked);
        Assert.Empty(RunSettings.From(Asked("--report-json", "/work/r.json"), file).IgnoreUntracked!);
    }

    /// <summary>Refused from the file too, and on any run, with the file named.</summary>
    [Fact]
    public void A_pattern_the_file_keeps_is_refused_with_the_file_named()
    {
        var file = new ConfigurationFile(IgnoreUntracked: ["*.cs"]) { Directory = "/work" };

        ArgumentException refusal = Assert.Throws<ArgumentException>(
            () => RunSettings.From(Asked("--report-json", "/work/r.json"), file));

        Assert.Contains("ignoreUntracked", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("killmutants.json", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_command_line_replaces_the_files_patterns()
    {
        var file = new ConfigurationFile(IgnoreUntracked: ["*.received.html"]) { Directory = "/work" };

        RunSettings settings = RunSettings.From(
            Asked("--resume", "0123abcd", "--report-json", "/work/r.json", "--ignore-untracked", "*.received.txt"),
            file);

        Assert.Equal(["*.received.txt"], settings.IgnoreUntracked);
    }
}
