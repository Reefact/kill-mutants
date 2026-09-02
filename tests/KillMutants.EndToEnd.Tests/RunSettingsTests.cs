using KillMutants.Cli;
using KillMutants.Mutations;

namespace KillMutants.EndToEnd.Tests;

/// <summary>
/// One rule, in one direction: the command line wins, otherwise the file, otherwise the default. So
/// the file states a project's habits and the command line states the exception.
/// </summary>
public class RunSettingsTests
{
    private static readonly MutatorName Comparison = MutatorName.Create("Comparison");
    private static readonly MutatorName StringLiteral = MutatorName.Create("StringLiteral");

    private static CommandLineOptions Asked(params string[] arguments) =>
        CommandLineOptions.Parse(["/work", .. arguments]);

    [Fact]
    public void With_no_file_and_no_options_the_defaults_stand()
    {
        RunSettings settings = RunSettings.From(Asked(), file: null);

        Assert.Equal("Release", settings.Configuration);
        Assert.True(settings.MeasureCoverage);
        Assert.Null(settings.WorkerCount);
        Assert.Null(settings.Threshold);
        Assert.Null(settings.JsonReportPath);
        Assert.Empty(settings.Exclude);
        Assert.Empty(settings.Mutators);
    }

    [Fact]
    public void The_file_supplies_what_the_command_line_did_not()
    {
        var file = new ConfigurationFile(
            Configuration: "Debug",
            Exclude: ["tests/fixtures/*"],
            Without: ["StringLiteral"],
            Parallel: 3,
            Coverage: false,
            BreakAt: 70);

        RunSettings settings = RunSettings.From(Asked(), file);

        Assert.Equal("Debug", settings.Configuration);
        Assert.Equal(["tests/fixtures/*"], settings.Exclude);
        Assert.Equal([StringLiteral], settings.WithoutMutators);
        Assert.Equal(3, settings.WorkerCount);
        Assert.False(settings.MeasureCoverage);
        Assert.Equal(70, settings.Threshold);
    }

    [Fact]
    public void Anything_given_on_the_command_line_wins()
    {
        var file = new ConfigurationFile(
            Configuration: "Debug", Parallel: 3, Coverage: true, BreakAt: 70);

        RunSettings settings = RunSettings.From(
            Asked("-c", "Release", "-p", "8", "--no-coverage", "--break-at", "90"), file);

        Assert.Equal("Release", settings.Configuration);
        Assert.Equal(8, settings.WorkerCount);
        Assert.False(settings.MeasureCoverage);
        Assert.Equal(90, settings.Threshold);
    }

    /// <summary>
    /// The reason every option on the command line is nullable. `--configuration Release` and saying
    /// nothing have to be told apart, or the defaults would silently outrank a file they never
    /// mentioned - here, by turning a project's `"configuration": "Debug"` back into Release.
    /// </summary>
    [Fact]
    public void Saying_nothing_is_not_the_same_as_asking_for_the_default()
    {
        var file = new ConfigurationFile(Configuration: "Debug", Coverage: false);

        Assert.Equal("Debug", RunSettings.From(Asked(), file).Configuration);
        Assert.False(RunSettings.From(Asked(), file).MeasureCoverage);
    }

    /// <summary>
    /// A list on the command line replaces the file's rather than adding to it, so there is a way to
    /// run without an exclusion the file states.
    /// </summary>
    [Fact]
    public void A_list_given_on_the_command_line_replaces_the_files()
    {
        var file = new ConfigurationFile(Exclude: ["a/*", "b/*"], Mutators: ["StringLiteral"]);

        RunSettings settings = RunSettings.From(Asked("-e", "c/*", "-m", "Comparison"), file);

        Assert.Equal(["c/*"], settings.Exclude);
        Assert.Equal([Comparison], settings.Mutators);
    }

    /// <summary>
    /// A path in the file means a place in that project, not wherever the shell happened to be.
    /// </summary>
    [Fact]
    public void A_report_path_in_the_file_is_relative_to_the_file()
    {
        var file = new ConfigurationFile(ReportJson: "artifacts/mutation.json") { Directory = "/work/repo" };

        RunSettings settings = RunSettings.From(Asked(), file);

        Assert.Equal(Path.GetFullPath("/work/repo/artifacts/mutation.json"), settings.JsonReportPath);
    }

    /// <summary>
    /// Checked once for both sources: a typo must not silently narrow a run, because a score only
    /// means something against the families that produced it.
    /// </summary>
    [Fact]
    public void An_unknown_family_in_the_file_is_refused_like_one_on_the_command_line()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => RunSettings.From(Asked(), new ConfigurationFile(Without: ["StringLiterals"])));

        Assert.Contains("StringLiterals", error.Message, StringComparison.Ordinal);
        Assert.Contains("Comparison", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The command line refuses these values at parse time, so the file was the only way in. The
    /// worst of them reached the session, which built no sandbox and then indexed the first one.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_worker_count_the_command_line_would_refuse_is_refused_in_the_file(int parallel)
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(
            () => RunSettings.From(Asked(), new ConfigurationFile(Parallel: parallel)));

        Assert.Contains("positive number of workers", refusal.Message, StringComparison.Ordinal);

        // And it says where to look, which a message about a setting read from a file has to.
        Assert.Contains("killmutants.json", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_negative_sample_of_kills_to_verify_is_refused_in_the_file()
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(
            () => RunSettings.From(Asked(), new ConfigurationFile(VerifyKills: -1)));

        Assert.Contains("verifyKills", refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1d)]
    [InlineData(101d)]
    public void A_threshold_outside_a_percentage_is_refused_in_the_file(double breakAt)
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(
            () => RunSettings.From(Asked(), new ConfigurationFile(BreakAt: breakAt)));

        Assert.Contains("percentage between 0 and 100", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>And the values at the edge of each rule are accepted, or the rules are too strict.</summary>
    [Fact]
    public void The_smallest_values_each_rule_allows_are_accepted()
    {
        RunSettings settings = RunSettings.From(
            Asked(), new ConfigurationFile(Parallel: 1, VerifyKills: 0, BreakAt: 0));

        Assert.Equal(1, settings.WorkerCount);
        Assert.Equal(0, settings.VerifyKills);
        Assert.Equal(0, settings.Threshold);
    }

    /// <summary>
    /// The documented rule is that a list on the command line replaces the file's. There was one
    /// list nobody could replace: an omitted option and an explicitly empty one both arrived as
    /// nothing, and an empty value was refused, so a `without` in the file applied to every run.
    /// Naming every family with --mutators did not help either - the exclusions came after.
    /// </summary>
    [Fact]
    public void The_command_line_can_clear_a_configured_exclusion_list()
    {
        var file = new ConfigurationFile(Without: ["StringLiteral", "BooleanLiteral"]);

        Assert.Equal(
            [StringLiteral, MutatorName.Create("BooleanLiteral")],
            RunSettings.From(Asked(), file).WithoutMutators);

        Assert.Empty(RunSettings.From(Asked("--without", "none"), file).WithoutMutators);
    }

    /// <summary>And it can clear a configured catalogue the same way, for the same reason.</summary>
    [Fact]
    public void The_command_line_can_clear_a_configured_catalogue()
    {
        var file = new ConfigurationFile(Mutators: ["Comparison"]);

        Assert.Equal([Comparison], RunSettings.From(Asked(), file).Mutators);
        Assert.Empty(RunSettings.From(Asked("--mutators", "none"), file).Mutators);
    }

    /// <summary>
    /// An empty value is still refused, and the message now says what to write instead - the option
    /// exists to be discoverable from its own error.
    /// </summary>
    [Fact]
    public void An_empty_list_is_still_refused_and_says_what_to_write()
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(
            () => Asked("--without", ""));

        Assert.Contains("'none' for no families at all", refusal.Message, StringComparison.Ordinal);
    }
}
