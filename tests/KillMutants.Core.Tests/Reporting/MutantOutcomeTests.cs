using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;
using KillMutants.Reporting;

namespace KillMutants.Core.Tests.Reporting;

/// <summary>
/// What each status is worth is decided in exactly one place, so these are the tests that pin the
/// score's definition down. The one that matters is `NoCoverage`: it is undetected, not excluded.
/// </summary>
public class MutantOutcomeTests
{
    [Theory]
    [InlineData(MutantStatus.Killed, MutantOutcome.Detected)]
    [InlineData(MutantStatus.Timeout, MutantOutcome.Detected)]
    [InlineData(MutantStatus.Survived, MutantOutcome.Undetected)]
    [InlineData(MutantStatus.NoCoverage, MutantOutcome.Undetected)]
    [InlineData(MutantStatus.CompileError, MutantOutcome.Untestable)]
    [InlineData(MutantStatus.Ignored, MutantOutcome.Ignored)]
    public void Each_status_means_one_thing_for_the_score(MutantStatus status, MutantOutcome expected)
    {
        Assert.Equal(expected, Result(status).Outcome);
    }

    /// <summary>
    /// The regression this whole model exists for. A run where nothing is covered used to report
    /// 100%: the uncovered mutants were excluded from the denominator, leaving nothing in it but the
    /// handful that were reachable. Adding untested code would have <em>raised</em> the score.
    /// </summary>
    [Fact]
    public void Uncovered_mutants_count_against_the_score()
    {
        var report = new MutationTestReport(
        [
            Result(MutantStatus.Killed),
            Result(MutantStatus.NoCoverage),
            Result(MutantStatus.NoCoverage),
            Result(MutantStatus.NoCoverage),
        ]);

        Assert.Equal(1, report.Detected);
        Assert.Equal(3, report.Undetected);
        Assert.Equal("25%", report.Score.ToString());
    }

    /// <summary>
    /// A mutant this tool could not build says nothing about the tests, so it stays out of the
    /// denominator entirely - unlike an uncovered one, which says a great deal.
    /// </summary>
    [Fact]
    public void A_mutant_that_could_not_be_built_is_left_out_of_the_score()
    {
        var report = new MutationTestReport([Result(MutantStatus.Killed), Result(MutantStatus.CompileError)]);

        Assert.Equal(1, report.Untestable);
        Assert.Equal("100%", report.Score.ToString());
    }

    [Fact]
    public void A_timeout_is_a_detection_and_an_uncovered_mutant_is_not()
    {
        var report = new MutationTestReport([Result(MutantStatus.Timeout), Result(MutantStatus.NoCoverage)]);

        Assert.Equal(1, report.Detected);
        Assert.Equal(1, report.Undetected);
        Assert.Equal("50%", report.Score.ToString());
    }

    [Fact]
    public void A_run_of_nothing_but_untestable_mutants_has_no_score()
    {
        var report = new MutationTestReport([Result(MutantStatus.CompileError)]);

        Assert.True(report.Score.IsUndefined);
    }

    private static readonly Mutant Mutant = new MutantGenerator(MutatorCatalog.Default)
        .Generate(TestCompilation.From("class C { bool M(int a) => a >= 18; }"))[0];

    private static MutantResult Result(MutantStatus status) => new(Mutant, status);

    /// <summary>
    /// DEC0026: a mutant the project chose not to hear about is outside the score, by the project's
    /// own choice rather than the tool's limitation, and so counted apart from the untestable ones.
    /// </summary>
    [Fact]
    public void A_mutant_the_project_ignored_is_left_out_of_the_score_and_counted_apart()
    {
        var report = new MutationTestReport(
            [Result(MutantStatus.Killed), Result(MutantStatus.Ignored), Result(MutantStatus.CompileError)]);

        Assert.Equal("100%", report.Score.ToString());
        Assert.Equal(1, report.Ignored);
        Assert.Equal(1, report.Untestable);
        Assert.False(report.IsInconclusive);
    }

    /// <summary>
    /// A selection whose only mutants the project silenced has nothing to answer for, as a change
    /// with no mutants has: it is not a selection that could not be tested (DEC0010).
    /// </summary>
    [Fact]
    public void A_report_of_ignored_mutants_only_is_not_inconclusive()
    {
        var report = new MutationTestReport([Result(MutantStatus.Ignored), Result(MutantStatus.Ignored)]);

        Assert.True(report.Score.IsUndefined);
        Assert.False(report.IsInconclusive);
        Assert.False(report.HasUndetected);
    }
}
