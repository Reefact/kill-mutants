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
        Assert.Equal(29, plan.Mutants!.Count);
        Assert.Equal(2, Assert.Single(plan.Runs).Verdicts);
        Assert.Equal(2, VerdictLog.Read(store.LogPath).Count);

        MutationTestReport completed;

        using (VerdictLog log = VerdictLog.Append(store.LogPath))
        {
            completed = await Session(new XUnitTestRunner(), log, Continue("nightly", store))
                .RunAsync(fixture.Root, TestContext.Current.CancellationToken);
        }

        Assert.True(completed.IsComplete);
        Assert.Equal(29, completed.Total);
        Assert.False(completed.Score.IsUndefined);

        RunSeries series = Assert.IsType<RunSeries>(completed.Series);

        Assert.Equal(2, series.Inherited);
        Assert.Equal(2, series.Runs.Count);
        Assert.Equal(27, series.Runs[^1].Verdicts);

        // The two inherited verdicts are the two the first run reached, in their place, as they were:
        // same status, same tests named.
        Assert.Equal(
            stopped.Results.Select(result => (result.Mutant.Key, result.Status, result.KilledBy.Count)),
            completed.Results.Take(2).Select(result => (result.Mutant.Key, result.Status, result.KilledBy.Count)));
        Assert.All(completed.Results.Take(2), result => Assert.NotEmpty(result.KilledBy));

        IReadOnlyList<LoggedVerdict> logged = VerdictLog.Read(store.LogPath);

        Assert.Equal(29, logged.Count);
        Assert.Equal(29, logged.Select(verdict => verdict.Key).Distinct(StringComparer.Ordinal).Count());
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

        MutationTestReport first;

        using (VerdictLog log = VerdictLog.Create(store.LogPath))
        {
            first = await Session(new XUnitTestRunner(), log, Start("nightly", store))
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
        Assert.Equal(29, again.Total);
        Assert.Equal(29, again.Series!.Inherited);
        Assert.Equal(0, again.Series.Runs[^1].Verdicts);
        Assert.Equal(29, VerdictLog.Read(store.LogPath).Count);
        Assert.Equal(first.Score.ToString(), again.Score.ToString());

        // Inherited as reached: the same verdict, with the same tests named, for every mutant.
        Assert.Equal(
            first.Results.Select(result => (result.Mutant.Key, result.Status, string.Join(",", result.KilledBy))),
            again.Results.Select(result => (result.Mutant.Key, result.Status, string.Join(",", result.KilledBy))));
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

        var counting = new CountingRunner(new XUnitTestRunner());

        using (VerdictLog log = VerdictLog.Append(store.LogPath))
        {
            var continuation = new RunContinuation(
                "nightly", Identity, tampered, VerdictLog.Read(store.LogPath), store);

            ResumeException refusal = await Assert.ThrowsAsync<ResumeException>(
                () => Session(counting, log, continuation)
                    .RunAsync(fixture.Root, TestContext.Current.CancellationToken));

            Assert.Contains("nightly", refusal.Message, StringComparison.Ordinal);
            Assert.Contains("not the ones the earlier run selected", refusal.Message, StringComparison.Ordinal);
            Assert.Contains("left as they were", refusal.Message, StringComparison.Ordinal);
        }

        // Refused before the baseline, so before any test ran.
        Assert.Equal(0, counting.Runs);
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
        Assert.Equal(27, again.Series!.Inherited);
        Assert.Equal(2, again.Series.Runs[^1].Verdicts);

        IReadOnlyList<LoggedVerdict> logged = VerdictLog.Read(store.LogPath);

        Assert.Equal(31, logged.Count);

        LoggedVerdict lastTimedOut = logged.Last(verdict => verdict.Key == timedOut);
        LoggedVerdict lastCrashed = logged.Last(verdict => verdict.Key == crashed);

        Assert.Equal(MutantStatus.Killed, lastTimedOut.Status);
        Assert.NotEmpty(lastCrashed.KilledBy);
        Assert.All(again.Results, result => Assert.Equal(MutantStatus.Killed, result.Status));

        // A third run reads the later line for each of the two keys, and so inherits everything.
        var counting = new CountingRunner(new XUnitTestRunner());
        MutationTestReport third;

        using (VerdictLog log = VerdictLog.Append(store.LogPath))
        {
            third = await Session(counting, log, Continue("nightly", store))
                .RunAsync(fixture.Root, TestContext.Current.CancellationToken);
        }

        Assert.Equal(0, counting.Runs);
        Assert.Equal(29, third.Series!.Inherited);
        Assert.Equal(3, third.Series.Runs.Count);
    }

    /// <summary>
    /// A disagreement <c>--verify-kills</c> finds is written to the plan as it is found, since the log
    /// holds verdicts and nothing else, and the next run of the series reports it still.
    /// </summary>
    [Fact]
    public async Task A_disagreement_found_by_re_verification_reaches_the_plan_and_the_next_run()
    {
        using var fixture = FixtureCopy.Create();
        RunPlanStore store = StoreIn(fixture);

        // Every re-test passes, so every sampled kill disagrees with itself.
        var disagreeing = new PassesOnReTestRunner(new XUnitTestRunner(), mutantRunsBeforePassing: 29);
        MutationTestReport first;

        using (VerdictLog log = VerdictLog.Create(store.LogPath))
        {
            first = await Session(disagreeing, log, Start("nightly", store), verifyKills: 2)
                .RunAsync(fixture.Root, TestContext.Current.CancellationToken);
        }

        Assert.Equal(2, first.Results.Count(result => result.Disagreement is not null));

        RunPlan plan = store.ReadPlan()!;

        Assert.Equal(2, Assert.Single(plan.Runs).Disagreements.Count);
        Assert.Equal(2, plan.Runs[0].KillsReVerified);

        MutationTestReport again;

        using (VerdictLog log = VerdictLog.Append(store.LogPath))
        {
            again = await Session(new XUnitTestRunner(), log, Continue("nightly", store))
                .RunAsync(fixture.Root, TestContext.Current.CancellationToken);
        }

        Assert.Equal(
            first.Results.Where(result => result.Disagreement is not null).Select(result => result.Mutant.Key),
            again.Results.Where(result => result.Disagreement is not null).Select(result => result.Mutant.Key));
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
        string key = plan.Mutants![3].Key;
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
                    new StopsOnMutantRunner(new XUnitTestRunner(), stop, stopOnMutantRun: 28),
                    log,
                    Start("nightly", store))
                .RunAsync(fixture.Root, TestContext.Current.CancellationToken, stop.Token);
        }

        Assert.Equal(27, VerdictLog.Read(store.LogPath).Count);

        var counting = new CountingRunner(new XUnitTestRunner());
        MutationTestReport again;

        using (VerdictLog log = VerdictLog.Append(store.LogPath))
        {
            again = await Session(counting, log, Continue("nightly", store), verifyKills: 5)
                .RunAsync(fixture.Root, TestContext.Current.CancellationToken);
        }

        Assert.True(again.IsComplete);
        Assert.Equal(27, again.Series!.Inherited);
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
        Assert.Equal(report.Total, plan.Mutants!.Count);
        Assert.Equal(report.Results.Select(result => result.Mutant.Key.ToString()), plan.Mutants.Select(mutant => mutant.Key));
        Assert.Equal("0123abcd", plan.Identity.Code!.Name);
        Assert.NotNull(plan.Identity.Fingerprint);
        Assert.Equal("xUnit 4.0.0", plan.Identity.TestFramework);
        Assert.Equal(["Sample.Library/Sample.Library.csproj <- Sample.Library.Tests/Sample.Library.Tests.csproj"], plan.Identity.TargetGraph);
    }

    /// <summary>
    /// A run can be stopped - a deadline, a signal - while it is still discovering, building or
    /// generating, and such a run writes a report. The plan has to be beside it all the same: written
    /// before anything is discovered, with no mutants yet, and listing them from the next run on.
    /// </summary>
    [Fact]
    public async Task A_series_stopped_before_it_selected_anything_leaves_a_plan_and_is_continued()
    {
        using var fixture = FixtureCopy.Create();
        RunPlanStore store = StoreIn(fixture);
        using var stop = new CancellationTokenSource();

        await stop.CancelAsync();

        MutationTestReport stopped;

        using (VerdictLog log = VerdictLog.Create(store.LogPath))
        {
            stopped = await Session(new XUnitTestRunner(), log, Start("nightly", store))
                .RunAsync(fixture.Root, TestContext.Current.CancellationToken, stop.Token);
        }

        Assert.False(stopped.IsComplete);
        Assert.Null(stopped.Stopped!.Selected);
        Assert.Single(stopped.Series!.Runs);

        RunPlan plan = Assert.IsType<RunPlan>(store.ReadPlan());

        Assert.Equal("nightly", plan.Series);
        Assert.Null(plan.Mutants);
        Assert.Null(plan.Identity.Fingerprint);
        Assert.Equal("0123abcd", plan.Identity.Code!.Name);

        PlannedRun only = Assert.Single(plan.Runs);

        Assert.Equal(0, only.Verdicts);
        Assert.Equal(0, only.LogLinesBefore);
        Assert.Empty(VerdictLog.Read(store.LogPath));

        MutationTestReport completed;

        using (VerdictLog log = VerdictLog.Append(store.LogPath))
        {
            completed = await Session(new XUnitTestRunner(), log, Continue("nightly", store))
                .RunAsync(fixture.Root, TestContext.Current.CancellationToken);
        }

        Assert.True(completed.IsComplete);
        Assert.Equal(0, completed.Series!.Inherited);
        Assert.Equal(2, completed.Series.Runs.Count);
        Assert.Equal(29, completed.Series.Runs[^1].Verdicts);
        Assert.Equal(29, store.ReadPlan()!.Mutants!.Count);
    }

    /// <summary>
    /// A continuing run stopped before it selected anything has not compared the half of the identity
    /// only a run knows, so the plan it leaves is the series', with this run added to it and nothing
    /// reached - not a preliminary one that would lose the population.
    /// </summary>
    [Fact]
    public async Task A_continuing_run_stopped_before_it_selected_anything_keeps_the_series_population()
    {
        using var fixture = FixtureCopy.Create();
        RunPlanStore store = StoreIn(fixture);

        using (VerdictLog log = VerdictLog.Create(store.LogPath))
        {
            await Session(new XUnitTestRunner(), log, Start("nightly", store))
                .RunAsync(fixture.Root, TestContext.Current.CancellationToken);
        }

        using var stop = new CancellationTokenSource();

        await stop.CancelAsync();

        MutationTestReport stopped;

        using (VerdictLog log = VerdictLog.Append(store.LogPath))
        {
            stopped = await Session(new XUnitTestRunner(), log, Continue("nightly", store))
                .RunAsync(fixture.Root, TestContext.Current.CancellationToken, stop.Token);
        }

        Assert.False(stopped.IsComplete);

        RunPlan plan = Assert.IsType<RunPlan>(store.ReadPlan());

        Assert.Equal(29, plan.Mutants!.Count);
        Assert.NotNull(plan.Identity.Fingerprint);
        Assert.Equal([29, 0], plan.Runs.Select(run => run.Verdicts));
        Assert.Equal([0, 29], plan.Runs.Select(run => run.LogLinesBefore));

        var counting = new CountingRunner(new XUnitTestRunner());
        MutationTestReport third;

        using (VerdictLog log = VerdictLog.Append(store.LogPath))
        {
            third = await Session(counting, log, Continue("nightly", store))
                .RunAsync(fixture.Root, TestContext.Current.CancellationToken);
        }

        Assert.Equal(0, counting.Runs);
        Assert.Equal(29, third.Series!.Inherited);
        Assert.Equal(3, third.Series.Runs.Count);
    }

    /// <summary>
    /// A run that is killed never reaches the stop handler: its plan says what it had written at the
    /// end of the last project it finished, while the log took verdicts after that. The log is the
    /// authority, so the next run recomputes every earlier run's count from where in the log each
    /// began, and the final report's runs sum to the series.
    /// </summary>
    [Fact]
    public async Task A_run_killed_before_it_recorded_its_count_has_it_recomputed_from_the_log()
    {
        using var fixture = FixtureCopy.Create();
        RunPlanStore store = StoreIn(fixture);
        using var stop = new CancellationTokenSource();

        using (VerdictLog log = VerdictLog.Create(store.LogPath))
        {
            await Session(
                    new StopsOnMutantRunner(new XUnitTestRunner(), stop, stopOnMutantRun: 3),
                    log,
                    Start("nightly", store))
                .RunAsync(fixture.Root, TestContext.Current.CancellationToken, stop.Token);
        }

        // As a killed run leaves it: two verdicts in the log, none in the plan.
        RunPlan plan = store.ReadPlan()!;

        store.WritePlan(plan with { Runs = [plan.Runs[0] with { Verdicts = 0 }] });

        Assert.Equal(2, VerdictLog.Read(store.LogPath).Count);

        MutationTestReport completed;

        using (VerdictLog log = VerdictLog.Append(store.LogPath))
        {
            completed = await Session(new XUnitTestRunner(), log, Continue("nightly", store))
                .RunAsync(fixture.Root, TestContext.Current.CancellationToken);
        }

        Assert.Equal([2, 27], completed.Series!.Runs.Select(run => run.Verdicts));
        Assert.Equal([0, 2], completed.Series.Runs.Select(run => run.LogLinesBefore));
        Assert.Equal(completed.Total, completed.Series.Runs.Sum(run => run.Verdicts));
        Assert.Equal([2, 26], store.ReadPlan()!.Runs.Select(run => run.Verdicts));
    }

    /// <summary>
    /// Runs normally through the baseline and the given number of mutant runs, then reports every
    /// further run as passing - which is what a flaky kill looks like when it is re-tested.
    /// </summary>
    private sealed class PassesOnReTestRunner(ITestRunner inner, int mutantRunsBeforePassing) : ITestRunner
    {
        private int _mutantRuns;

        public Task<IReadOnlyList<TestName>> DiscoverAsync(
            TestProject testProject, CancellationToken cancellationToken = default) =>
            inner.DiscoverAsync(testProject, cancellationToken);

        public Task<TestRunOutcome> RunAsync(TestRunRequest request, CancellationToken cancellationToken = default)
        {
            if (!request.StopOnFirstFailure ||
                Interlocked.Increment(ref _mutantRuns) <= mutantRunsBeforePassing)
            {
                return inner.RunAsync(request, cancellationToken);
            }

            return Task.FromResult(new TestRunOutcome(
                Total: 1, Failed: 0, Errors: 0, Duration: TimeSpan.FromMilliseconds(1), TimedOut: false));
        }
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
