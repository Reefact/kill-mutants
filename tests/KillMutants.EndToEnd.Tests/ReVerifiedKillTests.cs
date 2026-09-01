using KillMutants.Execution;
using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;
using KillMutants.Projects;
using KillMutants.Reporting;
using KillMutants.Testing;
using KillMutants.Testing.XUnit;

namespace KillMutants.EndToEnd.Tests;

/// <summary>
/// Everything else guards against a mutant wrongly reported as alive. This guards the other
/// direction, which is worse: a mutant wrongly reported as killed is a gap in the tests that gets
/// celebrated instead of fixed.
/// </summary>
/// <remarks>
/// The baseline is verified once, at the start; after that any failing test counts as a kill,
/// whatever made it fail. A flaky or order-dependent test produces exactly the symptom a field
/// report described on another tool - a kill in CI, a survivor locally, and no way to tell which was
/// right. The answer is not to reason about it but to re-run it.
/// </remarks>
[Collection(nameof(SerialEndToEnd))]
public class ReVerifiedKillTests
{
    [Fact]
    public async Task A_kill_that_does_not_reproduce_is_reported_as_a_disagreement()
    {
        using var fixture = FixtureCopy.Create();

        // Gives the run a mutant that genuinely survives - `quantity >= 10` shifted to `> 10`, which
        // the one test at 50 cannot tell apart - so there is a passing mutant run to spoil.
        fixture.AddPartlyTestedCode();

        // Fails once and passes ever after, which is what a flaky test looks like from here.
        var runner = new FailsOnceRunner(new XUnitTestRunner());

        MutationTestReport report = await Run(fixture.Root, runner, verifyKills: 20);

        Assert.True(runner.Injected, "the test never injected a failure, so it proved nothing");

        MutantResult disputed = Assert.Single(
            report.Results, result => result.Disagreement is not null);

        // The status is left alone: which of the two runs was right is not ours to decide.
        Assert.Equal(MutantStatus.Killed, disputed.Status);
        Assert.Contains("tested again", disputed.Disagreement!, StringComparison.Ordinal);

        // And it is said where a reader will see it, with the key needed to settle it by hand.
        RunWarning warning = Assert.Single(
            report.Warnings, w => w.Text.Contains("did not survive", StringComparison.Ordinal));

        Assert.Contains(disputed.Mutant.Key.ToString(), warning.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// And a run whose kills are sound says nothing, or the check would cry wolf on every project.
    /// </summary>
    [Fact]
    public async Task Kills_that_reproduce_are_reported_as_nothing_at_all()
    {
        using var fixture = FixtureCopy.Create();

        MutationTestReport report = await Run(fixture.Root, new XUnitTestRunner(), verifyKills: 20);

        Assert.All(report.Results, result => Assert.Null(result.Disagreement));
        Assert.DoesNotContain(
            report.Warnings, w => w.Text.Contains("did not survive", StringComparison.Ordinal));
    }

    private static Task<MutationTestReport> Run(string root, ITestRunner runner, int verifyKills) =>
        new MutationTestSession(
                runner,
                "Release",
                timeoutPolicy: null,
                workerCount: 1,
                measureCoverage: true,
                exclude: null,
                catalog: MutatorCatalog.Of([MutatorName.Create("Comparison")]),
                verifyKills: verifyKills)
            .RunAsync(root, TestContext.Current.CancellationToken);

    /// <summary>Reports one spurious failure, then behaves.</summary>
    /// <remarks>
    /// Placed on a mutant run that would otherwise have passed, so the mutant is reported killed for
    /// a reason the mutation did not cause - which is exactly the shape of the failure this check
    /// exists to catch. The baseline and the coverage pass are left alone: both must stay honest or
    /// the run aborts before reaching the part under test, and both are told apart by
    /// <c>StopOnFirstFailure</c>, which only a mutant run sets.
    /// </remarks>
    private sealed class FailsOnceRunner(ITestRunner inner) : ITestRunner
    {
        public bool Injected { get; private set; }

        public Task<IReadOnlyList<TestName>> DiscoverAsync(
            TestProject testProject, CancellationToken cancellationToken = default) =>
            inner.DiscoverAsync(testProject, cancellationToken);

        public async Task<TestRunOutcome> RunAsync(
            TestRunRequest request, CancellationToken cancellationToken = default)
        {
            TestRunOutcome outcome = await inner.RunAsync(request, cancellationToken);

            if (Injected || !request.StopOnFirstFailure || outcome.AnyFailed)
            {
                return outcome;
            }

            Injected = true;

            return outcome with
            {
                Failed = 1,
                FailedTests = [TestName.Create("Sample.Library.Tests.AgesTests.Flaky")],
            };
        }
    }
}
