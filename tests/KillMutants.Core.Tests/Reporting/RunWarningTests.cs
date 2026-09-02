using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;
using KillMutants.Reporting;

namespace KillMutants.Core.Tests.Reporting;

/// <summary>
/// Reconstructs the shape of a real report from another tool: 4 575 mutants, 2 505 of them counted
/// as detected only because they timed out, no survivors, and a headline of "100%". Half the
/// component had never been judged and nothing in the output invited anyone to doubt it.
/// </summary>
public class RunWarningTests
{
    private static readonly Mutant Mutant = new MutantGenerator(MutatorCatalog.Default)
        .Generate(TestCompilation.From("class C { bool M(int a) => a >= 18; }"))[0];

    [Fact]
    public void A_run_whose_score_rests_on_timeouts_says_so()
    {
        MutationTestReport report = Report(killed: 2070, timedOut: 2505, survived: 0);

        Assert.Equal("100%", report.Score.ToString());

        RunWarning warning = Assert.Single(report.Warnings);

        Assert.Contains("2505 of 4575", warning.Text, StringComparison.Ordinal);
        Assert.Contains("not because a test failed", warning.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// And it says it where the number is, so a reader who takes only the last lines away cannot
    /// take the score without the sentence that qualifies it.
    /// </summary>
    [Fact]
    public void The_warning_is_printed_before_the_score()
    {
        var writer = new StringWriter();

        ConsoleReportWriter.Write(writer, Report(killed: 2070, timedOut: 2505, survived: 0));

        string rendered = writer.ToString();

        Assert.Contains("timed out", rendered, StringComparison.Ordinal);
        Assert.InRange(
            rendered.IndexOf("timed out", StringComparison.Ordinal),
            0,
            rendered.IndexOf("Mutation score", StringComparison.Ordinal));
    }

    /// <summary>One timeout in four hundred detections is noise, and noise must stay quiet.</summary>
    [Fact]
    public void An_occasional_timeout_says_nothing()
    {
        Assert.Empty(Report(killed: 400, timedOut: 1, survived: 100).Warnings);
    }

    [Fact]
    public void No_timeout_at_all_says_nothing()
    {
        Assert.Empty(Report(killed: 10, timedOut: 0, survived: 3).Warnings);
    }

    /// <summary>
    /// The threshold is a judgement, so it is stated rather than buried, and measured against the
    /// share it actually names: timeouts over <em>detections</em>, which include the timeouts.
    /// </summary>
    [Fact]
    public void The_threshold_is_the_share_of_detections_the_constant_names()
    {
        Assert.Equal(0.1, RunWarning.TimeoutShareOfDetections);

        // 11 of 111 detections is 9.9%: below the line, and silent.
        Assert.Empty(Report(killed: 100, timedOut: 11, survived: 0).Warnings);

        // 12 of 112 is 10.7%: above it, and not.
        Assert.NotEmpty(Report(killed: 100, timedOut: 12, survived: 0).Warnings);
    }

    /// <summary>
    /// Mutants that could not be built are outside the score, so a reader has to be told how much of
    /// the run the score actually describes.
    /// </summary>
    [Fact]
    public void Mutants_left_out_of_the_score_are_declared()
    {
        MutationTestReport report = Report(killed: 5, timedOut: 0, survived: 5, compileErrors: 40);

        RunWarning warning = Assert.Single(report.Warnings);

        Assert.Contains("40 of 50", warning.Text, StringComparison.Ordinal);
        Assert.Contains("outside the score", warning.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A partial run prints no score, so no warning on one may name one.
    /// </summary>
    /// <remarks>
    /// Review found two that did: an assumption "carrying the score", and a verdict worth settling
    /// "before the score is believed". Both branches are emitted for partial reports too, so a run
    /// that deliberately has no number was telling its reader to trust or distrust one.
    /// </remarks>
    [Theory]
    [InlineData(2070, 2505, 0)]
    [InlineData(10, 0, 0)]
    public void No_warning_on_a_partial_run_mentions_a_score(int killed, int timedOut, int survived)
    {
        MutationTestReport report = Report(
            killed, timedOut, survived, compileErrors: 3, scope: Partial);

        Assert.NotEmpty(report.Warnings);

        foreach (RunWarning warning in report.Warnings)
        {
            Assert.DoesNotContain("score", warning.Text, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>And a full run still says "score", because that is what it prints.</summary>
    [Fact]
    public void A_full_run_still_talks_about_its_score()
    {
        MutationTestReport report = Report(killed: 2070, timedOut: 2505, survived: 0, compileErrors: 3);

        Assert.Contains(
            report.Warnings,
            warning => warning.Text.Contains("score", StringComparison.Ordinal));
    }

    private static readonly RunScope Partial = new("0123456789abcdef", "fedcba9876543210", false, 1);

    private static MutationTestReport Report(
        int killed, int timedOut, int survived, int compileErrors = 0, RunScope? scope = null)
    {
        List<MutantResult> results =
        [
            .. Enumerable.Repeat(new MutantResult(Mutant, MutantStatus.Killed), killed),
            .. Enumerable.Repeat(new MutantResult(Mutant, MutantStatus.Timeout), timedOut),
            .. Enumerable.Repeat(new MutantResult(Mutant, MutantStatus.Survived), survived),
            .. Enumerable.Repeat(new MutantResult(Mutant, MutantStatus.CompileError), compileErrors),
        ];

        return new MutationTestReport(results, scope: scope);
    }
}
