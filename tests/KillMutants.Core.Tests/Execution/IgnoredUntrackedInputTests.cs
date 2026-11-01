using KillMutants.Execution;
using KillMutants.Reporting;

namespace KillMutants.Core.Tests.Execution;

/// <summary>
/// DEC0025's second check: a file <c>--ignore-untracked</c> left out of the tree, and that the
/// compiler reads, refuses the run once the build has named its inputs - a pattern the list of
/// sensitive files could not judge, such as a directory of generated sources, is caught here.
/// </summary>
public sealed class IgnoredUntrackedInputTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("killmutants-ignored-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private PlanKeeper Keeper(RunPlan? earlier, params IgnoredFile[] ignored)
    {
        var identity = new RunIdentity(new CodeState("0123abcd", IsExact: true), "test-tool", "Release", true, [], [], []);
        var store = new RunPlanStore(Path.Combine(_directory, "report.json"));

        return new PlanKeeper(
            new RunContinuation("one", identity, earlier, [], store, ["Generated/*"], ignored),
            workerCount: 1);
    }

    private void Begin(PlanKeeper keeper, params string[] compiled) =>
        keeper.Begin([], [], [], RunScope.WholeCodebase, _directory, null, new VerdictLedger(null), compiled);

    [Fact]
    public void A_run_whose_compiler_reads_an_ignored_file_is_refused()
    {
        string generated = Path.Combine(_directory, "Generated", "Extra.g");
        PlanKeeper keeper = Keeper(earlier: null, new IgnoredFile("Generated/Extra.g", generated));

        ResumeException refused = Assert.Throws<ResumeException>(
            () => Begin(keeper, Path.Combine(_directory, "Library.cs"), generated));

        Assert.Contains("'Generated/Extra.g'", refused.Message, StringComparison.Ordinal);
        Assert.Contains("the compiler reads it", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Nothing was tested", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Review of #68: '*.dll' passes the sensitive list, so an untracked local reference a series was
    /// told to ignore is caught here, once the build has named it.
    /// </summary>
    [Fact]
    public void A_continuing_series_that_ignored_a_referenced_assembly_is_refused()
    {
        string library = Path.Combine(_directory, "libs", "Vendor.dll");
        var earlier = new RunPlan(
            RunPlan.CurrentFormat,
            "one",
            new RunIdentity(new CodeState("0123abcd", IsExact: true), "test-tool", "Release", true, [], [], []),
            Mutants: null,
            Runs: []);
        PlanKeeper keeper = Keeper(earlier, new IgnoredFile("libs/Vendor.dll", library));

        ResumeException refused = Assert.Throws<ResumeException>(
            () => Begin(keeper, Path.Combine(_directory, "Library.cs"), library));

        Assert.Contains("'libs/Vendor.dll'", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>A continuing series promises its plan and log as they were, and says so.</summary>
    [Fact]
    public void A_continuing_series_refused_for_it_keeps_its_plan()
    {
        string generated = Path.Combine(_directory, "Generated", "Extra.g");
        var earlier = new RunPlan(
            RunPlan.CurrentFormat,
            "one",
            new RunIdentity(new CodeState("0123abcd", IsExact: true), "test-tool", "Release", true, [], [], []),
            Mutants: null,
            Runs: []);
        PlanKeeper keeper = Keeper(earlier, new IgnoredFile("Generated/Extra.g", generated));

        ResumeException refused = Assert.Throws<ResumeException>(() => Begin(keeper, generated));

        Assert.Contains("The plan and the log were left as they were", refused.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_directory, "report.plan.json")));
    }

    [Fact]
    public void A_run_whose_compiler_reads_none_of_them_carries_on_and_records_them()
    {
        PlanKeeper keeper = Keeper(
            earlier: null,
            new IgnoredFile("Generated/Snapshot.received.html", Path.Combine(_directory, "Generated", "Snapshot.received.html")));

        Begin(keeper, Path.Combine(_directory, "Library.cs"));

        RunPlan plan = new RunPlanStore(Path.Combine(_directory, "report.json")).ReadPlan()!;
        PlannedRun run = Assert.Single(plan.Runs);

        Assert.Equal(["Generated/*"], run.IgnoreUntracked);
        Assert.Equal(["Generated/Snapshot.received.html"], run.IgnoredUntracked);
    }
}
