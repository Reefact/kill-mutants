using System.Text.Json;
using System.Text.Json.Nodes;
using KillMutants.Execution;
using KillMutants.Mutations;
using KillMutants.Projects;
using KillMutants.Reporting;
using KillMutants.Testing;
using KillMutants.Testing.XUnit;

namespace KillMutants.EndToEnd.Tests;

/// <summary>
/// A run that continues a series inherits the verdicts an earlier run reached on the same code and
/// judges the rest (DEC0015), keeping its plan beside the report as it goes (DEC0016). The code
/// state is handed in rather than read from git, so these run on a fixture copied outside any
/// working copy; what git says is the command line's business and is tested there.
/// </summary>
[Collection(nameof(SerialEndToEnd))]
public class ResumedRunTests
{
    private static readonly RunIdentity Identity = new(
        new CodeState("0123abcd", IsExact: true), "test-tool", "Release", Coverage: false, [], [], []);

    private static RunContinuation Start(string? series, RunPlanStore store) =>
        new(series, Identity, Earlier: null, [], store);

    private static RunContinuation Continue(string series, RunPlanStore store) =>
        new(series, Identity, store.ReadPlan(), VerdictLog.Read(store.LogPath), store);

    private static RunPlanStore StoreIn(FixtureCopy fixture) =>
        new(Path.Combine(fixture.Root, "out", "report.json"));

    private static MutationTestSession Session(
        ITestRunner runner, VerdictLog log, RunContinuation continuation, int verifyKills = 0) =>
        new(
            runner, "Release", timeoutPolicy: null, workerCount: 1, measureCoverage: false,
            verifyKills: verifyKills, verdicts: log, continuation: continuation);

    [Fact]
    public async Task A_stopped_run_is_continued_by_the_next_and_the_series_then_has_a_score()
    {
        using var fixture = FixtureCopy.Create();
        RunPlanStore store = StoreIn(fixture);
        using var stop = new CancellationTokenSource();

        MutationTestReport stopped;

        using (VerdictLog log = VerdictLog.Create(store.LogPath))
        {
            stopped = await Session(
                    new StopsOnMutantRunner(new XUnitTestRunner(), stop, stopOnMutantRun: 3),
                    log,
                    Start("nightly", store))
                .RunAsync(fixture.Root, TestContext.Current.CancellationToken, stop.Token);
        }

        Assert.False(stopped.IsComplete);
        Assert.Equal(2, stopped.Total);

        RunPlan plan = Assert.IsType<RunPlan>(store.ReadPlan());

        Assert.Equal("nightly", plan.Series);
        Assert.Equal(17, plan.Mutants.Count);
        Assert.Equal(2, Assert.Single(plan.Runs).Verdicts);
        Assert.Equal(2, VerdictLog.Read(store.LogPath).Count);

        MutationTestReport completed;

        using (VerdictLog log = VerdictLog.Append(store.LogPath))
        {
            completed = await Session(new XUnitTestRunner(), log, Continue("nightly", store))
                .RunAsync(fixture.Root, TestContext.Current.CancellationToken);
        }

        Assert.True(completed.IsComplete);
        Assert.Equal(17, completed.Total);
        Assert.False(completed.Score.IsUndefined);

        RunSeries series = Assert.IsType<RunSeries>(completed.Series);

        Assert.Equal(2, series.Inherited);
        Assert.Equal(2, series.Runs.Count);
        Assert.Equal(15, series.Runs[^1].Verdicts);

        // The two inherited verdicts are the two the first run reached, in their place.
        Assert.Equal(
            stopped.Results.Select(result => result.Mutant.Key),
            completed.Results.Take(2).Select(result => result.Mutant.Key));

        IReadOnlyList<LoggedVerdict> logged = VerdictLog.Read(store.LogPath);

        Assert.Equal(17, logged.Count);
        Assert.Equal(17, logged.Select(verdict => verdict.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, store.ReadPlan()!.Runs.Count);

        var json = new StringWriter();
        JsonReportWriter.Write(json, completed);

        using JsonDocument document = JsonDocument.Parse(json.ToString());
        JsonElement reported = document.RootElement.GetProperty("series");

        Assert.Equal("nightly", reported.GetProperty("id").GetString());
        Assert.Equal(2, reported.GetProperty("inherited").GetInt32());
        Assert.Equal(2, reported.GetProperty("runs").GetArrayLength());
        Assert.True(document.RootElement.GetProperty("complete").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, document.RootElement.GetProperty("score").ValueKind);
    }

    /// <summary>
    /// A project with nothing left pays nothing: no sandbox, no baseline, no test run at all. This is
    /// what lets a series reach the projects a single run never got to.
    /// </summary>
    [Fact]
    public async Task A_run_that_inherits_everything_runs_no_test()
    {
        using var fixture = FixtureCopy.Create();
        RunPlanStore store = StoreIn(fixture);

        using (VerdictLog log = VerdictLog.Create(store.LogPath))
        {
            await Session(new XUnitTestRunner(), log, Start("nightly", store))
                .RunAsync(fixture.Root, TestContext.Current.CancellationToken);
        }

        var counting = new CountingRunner(new XUnitTestRunner());
        MutationTestReport again;

        using (VerdictLog log = VerdictLog.Append(store.LogPath))
        {
            again = await Session(counting, log, Continue("nightly", store))
                .RunAsync(fixture.Root, TestContext.Current.CancellationToken);
        }

        Assert.Equal(0, counting.Runs);
        Assert.True(again.IsComplete);
        Assert.Equal(17, again.Total);
        Assert.Equal(17, again.Series!.Inherited);
        Assert.Equal(0, again.Series.Runs[^1].Verdicts);
        Assert.Equal(17, VerdictLog.Read(store.LogPath).Count);
    }

    /// <summary>
    /// Everything known before the run matched, or the log would not have been continued; what
    /// differs here can only come from the build or the generation. Nothing is emptied on the way
    /// out, and the message names the difference.
    /// </summary>
    [Fact]
    public async Task A_series_whose_population_differs_is_refused_with_nothing_touched()
    {
        using var fixture = FixtureCopy.Create();
        RunPlanStore store = StoreIn(fixture);

        using (VerdictLog log = VerdictLog.Create(store.LogPath))
        {
            await Session(new XUnitTestRunner(), log, Start("nightly", store))
                .RunAsync(fixture.Root, TestContext.Current.CancellationToken);
        }

        RunPlan earlier = store.ReadPlan()!;
        RunPlan tampered = earlier with { Identity = earlier.Identity with { Fingerprint = "not-these" } };
        string planBefore = File.ReadAllText(store.PlanPath);
        int linesBefore = VerdictLog.Read(store.LogPath).Count;

        using (VerdictLog log = VerdictLog.Append(store.LogPath))
        {
            var continuation = new RunContinuation(
                "nightly", Identity, tampered, VerdictLog.Read(store.LogPath), store);

            ResumeException refusal = await Assert.ThrowsAsync<ResumeException>(
                () => Session(new XUnitTestRunner(), log, continuation)
                    .RunAsync(fixture.Root, TestContext.Current.CancellationToken));

            Assert.Contains("nightly", refusal.Message, StringComparison.Ordinal);
            Assert.Contains("not the ones the earlier run selected", refusal.Message, StringComparison.Ordinal);
        }

        Assert.Equal(planBefore, File.ReadAllText(store.PlanPath));
        Assert.Equal(linesBefore, VerdictLog.Read(store.LogPath).Count);
    }

    /// <summary>
    /// A timeout is final only under the budget of the run that confirmed it, and a kill no test is
    /// named for is a crash the stop itself can cause. Neither is inherited; both are judged again,
    /// and the later line in the log is the one that counts.
    /// </summary>
    [Fact]
    public async Task A_timeout_and_a_kill_no_test_is_named_for_are_judged_again()
    {
        using var fixture = FixtureCopy.Create();
        RunPlanStore store = StoreIn(fixture);

        using (VerdictLog log = VerdictLog.Create(store.LogPath))
        {
            await Session(new XUnitTestRunner(), log, Start("nightly", store))
                .RunAsync(fixture.Root, TestContext.Current.CancellationToken);
        }

        string[] lines = File.ReadAllLines(store.LogPath);
        JsonNode first = JsonNode.Parse(lines[0])!;
        JsonNode second = JsonNode.Parse(lines[1])!;

        first["status"] = "Timeout";
        second["killedBy"] = new JsonArray();
        lines[0] = first.ToJsonString();
        lines[1] = second.ToJsonString();
        File.WriteAllLines(store.LogPath, lines);

        string timedOut = first["key"]!.GetValue<string>();
        string crashed = second["key"]!.GetValue<string>();

        MutationTestReport again;

        using (VerdictLog log = VerdictLog.Append(store.LogPath))
        {
            again = await Session(new XUnitTestRunner(), log, Continue("nightly", store))
                .RunAsync(fixture.Root, TestContext.Current.CancellationToken);
        }

        Assert.True(again.IsComplete);
        Assert.Equal(15, again.Series!.Inherited);
        Assert.Equal(2, again.Series.Runs[^1].Verdicts);

        IReadOnlyList<LoggedVerdict> logged = VerdictLog.Read(store.LogPath);

        Assert.Equal(19, logged.Count);

        LoggedVerdict lastTimedOut = logged.Last(verdict => verdict.Key == timedOut);
        LoggedVerdict lastCrashed = logged.Last(verdict => verdict.Key == crashed);

        Assert.Equal(MutantStatus.Killed, lastTimedOut.Status);
        Assert.NotEmpty(lastCrashed.KilledBy);
        Assert.All(again.Results, result => Assert.Equal(MutantStatus.Killed, result.Status));
    }

    /// <summary>A disagreement lives in the plan, not the log, and the series keeps reporting it.</summary>
    [Fact]
    public async Task A_disagreement_an_earlier_run_found_is_still_reported_by_the_series()
    {
        using var fixture = FixtureCopy.Create();
        RunPlanStore store = StoreIn(fixture);

        using (VerdictLog log = VerdictLog.Create(store.LogPath))
        {
            await Session(new XUnitTestRunner(), log, Start("nightly", store))
                .RunAsync(fixture.Root, TestContext.Current.CancellationToken);
        }

        RunPlan plan = store.ReadPlan()!;
        string key = plan.Mutants[3].Key;
        const string disagreement = "reported Killed and then Survived when tested again on its own.";

        store.WritePlan(plan with
        {
            Runs = [plan.Runs[0] with { Disagreements = [new PlannedDisagreement(key, disagreement)] }],
        });

        MutationTestReport again;

        using (VerdictLog log = VerdictLog.Append(store.LogPath))
        {
            again = await Session(new XUnitTestRunner(), log, Continue("nightly", store))
                .RunAsync(fixture.Root, TestContext.Current.CancellationToken);
        }

        MutantResult disputed = Assert.Single(again.Results, result => result.Mutant.Key.ToString() == key);

        Assert.Equal(disagreement, disputed.Disagreement);
        Assert.Single(again.Results, result => result.Disagreement is not null);
    }

    /// <summary>
    /// The sample is drawn from every kill the project has a verdict for, inherited ones included, so
    /// a run that completes a series still checks the series' kills rather than only its own tail.
    /// </summary>
    [Fact]
    public async Task Re_verification_samples_the_inherited_kills_too()
    {
        using var fixture = FixtureCopy.Create();
        RunPlanStore store = StoreIn(fixture);
        using var stop = new CancellationTokenSource();

        using (VerdictLog log = VerdictLog.Create(store.LogPath))
        {
            await Session(
                    new StopsOnMutantRunner(new XUnitTestRunner(), stop, stopOnMutantRun: 16),
                    log,
                    Start("nightly", store))
                .RunAsync(fixture.Root, TestContext.Current.CancellationToken, stop.Token);
        }

        Assert.Equal(15, VerdictLog.Read(store.LogPath).Count);

        var counting = new CountingRunner(new XUnitTestRunner());
        MutationTestReport again;

        using (VerdictLog log = VerdictLog.Append(store.LogPath))
        {
            again = await Session(counting, log, Continue("nightly", store), verifyKills: 5)
                .RunAsync(fixture.Root, TestContext.Current.CancellationToken);
        }

        Assert.True(again.IsComplete);
        Assert.Equal(15, again.Series!.Inherited);
        Assert.Equal(5, again.Environment!.KillsReVerified);

        // The baseline, the two mutants left, and five re-tests: more than the two fresh kills alone
        // could have supplied.
        Assert.Equal(1 + 2 + 5, counting.Runs);
    }

    [Fact]
    public async Task A_run_that_names_no_series_still_leaves_its_plan()
    {
        using var fixture = FixtureCopy.Create();
        RunPlanStore store = StoreIn(fixture);
        MutationTestReport report;

        using (VerdictLog log = VerdictLog.Create(store.LogPath))
        {
            report = await Session(new XUnitTestRunner(), log, Start(series: null, store))
                .RunAsync(fixture.Root, TestContext.Current.CancellationToken);
        }

        RunPlan plan = Assert.IsType<RunPlan>(store.ReadPlan());

        Assert.Null(plan.Series);
        Assert.Null(report.Series);
        Assert.Equal(report.Total, plan.Mutants.Count);
        Assert.Equal(report.Results.Select(result => result.Mutant.Key.ToString()), plan.Mutants.Select(mutant => mutant.Key));
        Assert.Equal("0123abcd", plan.Identity.Code!.Name);
        Assert.NotNull(plan.Identity.Fingerprint);
        Assert.Equal("xUnit 4.0.0", plan.Identity.TestFramework);
        Assert.Equal(["Sample.Library/Sample.Library.csproj <- Sample.Library.Tests/Sample.Library.Tests.csproj"], plan.Identity.TargetGraph);
    }

    /// <summary>Counts the test runs it is asked for, and delegates every one of them.</summary>
    private sealed class CountingRunner(ITestRunner inner) : ITestRunner
    {
        private int _runs;

        public int Runs => _runs;

        public Task<IReadOnlyList<TestName>> DiscoverAsync(
            TestProject testProject, CancellationToken cancellationToken = default) =>
            inner.DiscoverAsync(testProject, cancellationToken);

        public Task<TestRunOutcome> RunAsync(TestRunRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _runs);

            return inner.RunAsync(request, cancellationToken);
        }
    }

    /// <summary>
    /// Runs normally until the given mutant run, then asks for the stop and reports that run as the
    /// crash a host killed by the same signal would leave.
    /// </summary>
    private sealed class StopsOnMutantRunner(
        ITestRunner inner, CancellationTokenSource stop, int stopOnMutantRun) : ITestRunner
    {
        private int _mutantRuns;

        public Task<IReadOnlyList<TestName>> DiscoverAsync(
            TestProject testProject, CancellationToken cancellationToken = default) =>
            inner.DiscoverAsync(testProject, cancellationToken);

        public async Task<TestRunOutcome> RunAsync(
            TestRunRequest request, CancellationToken cancellationToken = default)
        {
            if (!request.StopOnFirstFailure ||
                Interlocked.Increment(ref _mutantRuns) != stopOnMutantRun)
            {
                return await inner.RunAsync(request, cancellationToken);
            }

            await stop.CancelAsync();

            return TestRunOutcome.FromCrash(TimeSpan.Zero, "killed by the stop");
        }
    }
}
