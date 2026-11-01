using KillMutants.Cli;

namespace KillMutants.EndToEnd.Tests;

/// <summary>
/// DEC0023: the console progress is chosen by <c>--progress</c>, <c>log</c> unless asked, and nothing
/// about the terminal is detected; <c>--slow</c> sets when a mutant in progress is named (#65).
/// </summary>
public class ProgressSettingsTests
{
    private static CommandLineOptions Asked(params string[] arguments) =>
        CommandLineOptions.Parse(["/work", .. arguments]);

    [Fact]
    public void The_default_is_the_log()
    {
        RunSettings settings = RunSettings.From(Asked(), file: null);

        Assert.Equal(ProgressMode.Log, settings.Progress);
        Assert.Null(settings.Slow);
    }

    [Theory]
    [InlineData("log", "Log")]
    [InlineData("live", "Live")]
    [InlineData("NONE", "None")]
    public void Each_mode_is_named_by_its_word(string written, string mode)
    {
        Assert.Equal(mode, RunSettings.From(Asked("--progress", written), file: null).Progress.ToString());
    }

    [Theory]
    [InlineData("quiet")]
    [InlineData("1")]
    public void Another_word_is_refused(string written)
    {
        ArgumentException refused = Assert.Throws<ArgumentException>(() => Asked("--progress", written));

        Assert.Contains("log, live or none", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_mode_needs_a_value()
    {
        Assert.Throws<ArgumentException>(() => Asked("--progress"));
    }

    [Fact]
    public void The_file_keeps_the_mode_and_the_command_line_wins()
    {
        var file = new ConfigurationFile(Progress: "live", Slow: "45s") { Directory = "/work" };

        RunSettings fromFile = RunSettings.From(Asked(), file);
        RunSettings asked = RunSettings.From(Asked("--progress", "none", "--slow", "2m"), file);

        Assert.Equal(ProgressMode.Live, fromFile.Progress);
        Assert.Equal(TimeSpan.FromSeconds(45), fromFile.Slow);
        Assert.Equal(ProgressMode.None, asked.Progress);
        Assert.Equal(TimeSpan.FromMinutes(2), asked.Slow);
    }

    /// <summary>A word the file cannot mean is refused, not dropped for the default without a word.</summary>
    [Fact]
    public void A_mode_the_file_cannot_mean_is_refused()
    {
        var file = new ConfigurationFile(Progress: "quiet") { Directory = "/work" };

        ArgumentException refused = Assert.Throws<ArgumentException>(() => RunSettings.From(Asked(), file));

        Assert.Contains("'progress' in killmutants.json", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0s")]
    [InlineData("30")]
    [InlineData("soon")]
    public void A_threshold_that_is_not_a_duration_is_refused(string written)
    {
        Assert.Throws<ArgumentException>(() => Asked("--slow", written));
        Assert.Throws<ArgumentException>(() => RunSettings.From(
            Asked(), new ConfigurationFile(Slow: written) { Directory = "/work" }));
    }
}
