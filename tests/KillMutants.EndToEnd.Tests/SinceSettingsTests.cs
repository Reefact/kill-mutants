using KillMutants.Cli;

namespace KillMutants.EndToEnd.Tests;

/// <summary>
/// A threshold means a score, and a partial run has none. What matters as much as the refusal is
/// where the message sends the reader, and that there is a way out that is not editing a versioned
/// file.
/// </summary>
public class SinceSettingsTests
{
    private static CommandLineOptions Asked(params string[] arguments) =>
        CommandLineOptions.Parse(["/work", .. arguments]);

    [Fact]
    public void A_revision_is_carried_through_to_the_run()
    {
        Assert.Equal("origin/main", RunSettings.From(Asked("--since", "origin/main"), file: null).Since);
    }

    [Fact]
    public void Without_the_option_a_run_measures_the_whole_codebase()
    {
        Assert.Null(RunSettings.From(Asked(), file: null).Since);
    }

    [Theory]
    [InlineData("--since")]
    public void The_option_needs_a_revision(string option)
    {
        Assert.Throws<ArgumentException>(() => Asked(option));
    }

    [Fact]
    public void A_threshold_on_the_command_line_is_refused_with_since()
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(
            () => RunSettings.From(Asked("--since", "main", "--break-at", "60"), file: null));

        Assert.Contains("--break-at", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("--since", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The sharp one. <c>breakAt</c> in the file is exactly what the README tells a project to keep
    /// there, so a refusal that named only the option would send the reader to a command line that
    /// does not mention a threshold - and every partial run in that repository would be refused.
    /// </summary>
    [Fact]
    public void A_threshold_from_the_file_is_refused_and_the_file_is_named()
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(
            () => RunSettings.From(Asked("--since", "main"), new ConfigurationFile(BreakAt: 60)));

        Assert.Contains(ConfigurationFile.Name, refusal.Message, StringComparison.Ordinal);
        Assert.Contains("breakAt", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("--break-at none", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>The way out, and the same shape as <c>--without none</c>.</summary>
    [Fact]
    public void Break_at_none_clears_the_file_and_lets_the_partial_run_through()
    {
        RunSettings settings = RunSettings.From(
            Asked("--since", "main", "--break-at", "none"), new ConfigurationFile(BreakAt: 60));

        Assert.Null(settings.Threshold);
        Assert.Equal("main", settings.Since);
    }

    /// <summary>
    /// And it clears a threshold on a full run too, which is what makes it one rule rather than a
    /// special case bolted on to <c>--since</c>.
    /// </summary>
    [Fact]
    public void Break_at_none_clears_the_file_on_a_full_run_as_well()
    {
        RunSettings settings = RunSettings.From(
            Asked("--break-at", "none"), new ConfigurationFile(BreakAt: 60));

        Assert.Null(settings.Threshold);
        Assert.Null(settings.Since);
    }

    /// <summary>
    /// A scalar option is last-one-wins, and clearing it must not outlive the value that follows.
    /// </summary>
    /// <remarks>
    /// Review found this. <c>--break-at none --break-at 80</c> kept the clear marker and resolved to
    /// no threshold at all, so a job that asked for a gate twice would have had none - and would have
    /// been allowed to combine it with <c>--since</c> besides. A quality gate that disarms itself in
    /// silence is the one failure this must not have.
    /// </remarks>
    [Fact]
    public void A_later_threshold_undoes_an_earlier_clear()
    {
        RunSettings settings = RunSettings.From(
            Asked("--break-at", "none", "--break-at", "80"), file: null);

        Assert.Equal(80, settings.Threshold);
    }

    /// <summary>And the restored threshold is refused with <c>--since</c> like any other.</summary>
    [Fact]
    public void A_threshold_restored_after_a_clear_is_still_refused_with_since()
    {
        Assert.Throws<ArgumentException>(
            () => RunSettings.From(
                Asked("--since", "main", "--break-at", "none", "--break-at", "80"), file: null));
    }

    /// <summary>Clearing last still clears, which is the order that means it.</summary>
    [Fact]
    public void A_clear_after_a_threshold_still_clears()
    {
        RunSettings settings = RunSettings.From(
            Asked("--break-at", "80", "--break-at", "none"), file: null);

        Assert.Null(settings.Threshold);
    }

    [Fact]
    public void A_threshold_still_reaches_a_full_run_from_either_source()
    {
        Assert.Equal(60, RunSettings.From(Asked("--break-at", "60"), file: null).Threshold);
        Assert.Equal(70, RunSettings.From(Asked(), new ConfigurationFile(BreakAt: 70)).Threshold);
    }
}
