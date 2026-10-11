using KillMutants.Cli;

namespace KillMutants.EndToEnd.Tests;

/// <summary>
/// The deadline a run is given, and the one name its report may no longer take. A deadline that is
/// misread is worse than none: it stops a run almost at once, or lets it run until the CI that was
/// meant to be beaten kills it.
/// </summary>
public class StopAfterSettingsTests
{
    private static CommandLineOptions Asked(params string[] arguments) =>
        CommandLineOptions.Parse(["/work", .. arguments]);

    [Theory]
    [InlineData("85m", 85 * 60)]
    [InlineData("90s", 90)]
    [InlineData("2h", 2 * 3600)]
    [InlineData("1h30m", 90 * 60)]
    [InlineData("1h0m5s", 3605)]
    [InlineData("10M", 600)]
    public void A_deadline_is_read_with_its_unit(string text, int seconds)
    {
        Assert.True(Duration.TryParse(text, out TimeSpan deadline));
        Assert.Equal(TimeSpan.FromSeconds(seconds), deadline);
    }

    /// <summary>
    /// A bare number is the refusal that matters most: read as seconds or as minutes, one of the two
    /// readings is wrong by a factor of sixty, and nothing at run time would say so.
    /// </summary>
    [Theory]
    [InlineData("85")]
    [InlineData("0m")]
    [InlineData("0s")]
    [InlineData("m")]
    [InlineData("1x")]
    [InlineData("30m1h")]
    [InlineData("1h1h")]
    [InlineData("-5m")]
    [InlineData("1.5h")]
    [InlineData("")]
    [InlineData("100000000h")]
    public void A_deadline_without_a_unit_or_out_of_range_is_refused(string text)
    {
        Assert.False(Duration.TryParse(text, out _));
    }

    [Theory]
    [InlineData("85m")]
    [InlineData("90s")]
    [InlineData("1h30m")]
    [InlineData("1h0m5s")]
    public void A_deadline_is_written_back_in_the_form_it_is_read(string text)
    {
        Assert.True(Duration.TryParse(text, out TimeSpan deadline));
        Assert.True(Duration.TryParse(Duration.Format(deadline), out TimeSpan again));
        Assert.Equal(deadline, again);
    }

    [Fact]
    public void The_command_line_sets_a_deadline()
    {
        Assert.Equal(
            TimeSpan.FromMinutes(85),
            RunSettings.From(Asked("--stop-after", "85m"), file: null).StopAfter);
    }

    [Fact]
    public void Without_a_deadline_a_run_has_none()
    {
        Assert.Null(RunSettings.From(Asked(), file: null).StopAfter);
    }

    [Fact]
    public void The_file_supplies_a_deadline_and_the_command_line_overrides_it()
    {
        var file = new ConfigurationFile(StopAfter: "1h");

        Assert.Equal(TimeSpan.FromHours(1), RunSettings.From(Asked(), file).StopAfter);
        Assert.Equal(
            TimeSpan.FromMinutes(5),
            RunSettings.From(Asked("--stop-after", "5m"), file).StopAfter);
    }

    [Fact]
    public void The_command_line_can_clear_a_configured_deadline()
    {
        var file = new ConfigurationFile(StopAfter: "1h");

        Assert.Null(RunSettings.From(Asked("--stop-after", "none"), file).StopAfter);
    }

    /// <summary>Last one wins, both ways, as for --break-at.</summary>
    [Fact]
    public void A_later_deadline_overrides_an_earlier_none()
    {
        var file = new ConfigurationFile(StopAfter: "1h");

        Assert.Equal(
            TimeSpan.FromMinutes(5),
            RunSettings.From(Asked("--stop-after", "none", "--stop-after", "5m"), file).StopAfter);
    }

    [Fact]
    public void A_deadline_the_command_line_cannot_read_is_refused()
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(
            () => Asked("--stop-after", "85"));

        Assert.Contains("85m", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_deadline_the_file_cannot_express_is_refused_and_named()
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(
            () => RunSettings.From(Asked(), new ConfigurationFile(StopAfter: "85")));

        Assert.Contains("'stopAfter' in killmutants.json", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The verdict log takes the report's name with <c>.jsonl</c>, so a report already called that
    /// would be the log - and would be replaced by the report the moment the run finished.
    /// </summary>
    [Fact]
    public void A_report_named_like_its_verdict_log_is_refused()
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(
            () => RunSettings.From(Asked("--report-json", "/work/report.jsonl"), file: null));

        Assert.Contains("'--report-json'", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(".jsonl", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_report_named_like_its_verdict_log_in_the_file_is_refused_and_named()
    {
        var file = new ConfigurationFile(ReportJson: "artifacts/mutation.JSONL") { Directory = "/work" };

        ArgumentException refusal = Assert.Throws<ArgumentException>(
            () => RunSettings.From(Asked(), file));

        Assert.Contains("'reportJson' in killmutants.json", refusal.Message, StringComparison.Ordinal);
    }
}
