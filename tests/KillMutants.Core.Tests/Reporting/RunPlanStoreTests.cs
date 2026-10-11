using KillMutants.Reporting;

namespace KillMutants.Core.Tests.Reporting;

/// <summary>
/// The plan is what a later run, or a person, reads to know what a run set out to judge (DEC0016),
/// so what is written has to come back whole, and what cannot be read has to say so.
/// </summary>
public sealed class RunPlanStoreTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("killmutants-test-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private RunPlanStore Store() => new(Path.Combine(_directory, "report.json"));

    private static RunPlan Plan(string? series = "nightly") => new(
        RunPlan.CurrentFormat,
        series,
        new RunIdentity(
            new CodeState("0123abcd", IsExact: true),
            "0.1.0-alpha",
            "Release",
            Coverage: true,
            ["Comparison"],
            [],
            ["tests/fixtures/*"],
            new PlannedScope("full", null, null, true),
            "fingerprint",
            ["src/Core.csproj <- tests/Core.Tests.csproj"],
            "xUnit 4.0.0"),
        [new PlannedMutant("7223f8dbce52736a", "M1", "Comparison", "src/Ages.cs", 3, 17, "a >= 18", "a > 18")],
        [new PlannedRun("2026-10-11T20:00:00.0000000+00:00", "0.1.0-alpha", ".NET 10", "Linux", 4, 2, 1, 0,
            [new PlannedDisagreement("7223f8dbce52736a", "reported Killed and then Survived")])]);

    [Theory]
    [InlineData("report.json", "report.plan.json")]
    [InlineData("mutation", "mutation.plan.json")]
    [InlineData("out/mutation.JSON", "out/mutation.plan.json")]
    public void The_plan_takes_the_reports_name_with_its_own_extension(string report, string expected)
    {
        Assert.Equal(expected, RunPlanStore.PlanPathBeside(report));
    }

    [Fact]
    public void The_three_files_share_a_name()
    {
        RunPlanStore store = Store();

        Assert.Equal(
            [store.ReportPath, store.LogPath, store.PlanPath],
            store.Files);
        Assert.EndsWith("report.jsonl", store.LogPath, StringComparison.Ordinal);
        Assert.EndsWith("report.plan.json", store.PlanPath, StringComparison.Ordinal);
    }

    [Fact]
    public void A_plan_written_is_read_back_whole()
    {
        RunPlanStore store = Store();
        RunPlan written = Plan();

        store.WritePlan(written);

        RunPlan read = Assert.IsType<RunPlan>(store.ReadPlan());

        // Field by field: the lists come back as lists, and a record compares them by reference.
        Assert.Equal(written.Format, read.Format);
        Assert.Equal(written.Series, read.Series);
        Assert.Equal(written.Identity.Code, read.Identity.Code);
        Assert.Equal(written.Identity.ToolVersion, read.Identity.ToolVersion);
        Assert.Equal(written.Identity.Configuration, read.Identity.Configuration);
        Assert.Equal(written.Identity.Coverage, read.Identity.Coverage);
        Assert.Equal(written.Identity.Mutators, read.Identity.Mutators);
        Assert.Equal(written.Identity.Without, read.Identity.Without);
        Assert.Equal(written.Identity.Exclude, read.Identity.Exclude);
        Assert.Equal(written.Identity.Scope, read.Identity.Scope);
        Assert.Equal(written.Identity.Fingerprint, read.Identity.Fingerprint);
        Assert.Equal(written.Identity.TargetGraph, read.Identity.TargetGraph);
        Assert.Equal(written.Identity.TestFramework, read.Identity.TestFramework);
        Assert.Equal(written.Mutants, read.Mutants);
        Assert.Equal(written.Runs.Count, read.Runs.Count);
        Assert.Equal(written.Runs[0] with { Disagreements = [] }, read.Runs[0] with { Disagreements = [] });
        Assert.Equal(written.Runs[0].Disagreements, read.Runs[0].Disagreements);
        Assert.Null(written.Identity.WhyNotContinuedBy(read.Identity));
    }

    /// <summary>The mutated operators are the payload, and must stay searchable in the file.</summary>
    [Fact]
    public void The_operators_are_written_as_they_are()
    {
        RunPlanStore store = Store();

        store.WritePlan(Plan());

        Assert.Contains("\"a >= 18\"", File.ReadAllText(store.PlanPath), StringComparison.Ordinal);
    }

    [Fact]
    public void No_plan_reads_as_none()
    {
        Assert.Null(Store().ReadPlan());
    }

    [Fact]
    public void Writing_leaves_no_half_written_file_behind()
    {
        RunPlanStore store = Store();

        store.WritePlan(Plan());
        store.WritePlan(Plan(series: "second"));

        Assert.Equal(["report.plan.json"], Directory.GetFiles(_directory).Select(Path.GetFileName));
        Assert.Equal("second", store.ReadPlan()!.Series);
    }

    [Fact]
    public void A_plan_that_is_not_one_is_refused_and_named()
    {
        RunPlanStore store = Store();

        Directory.CreateDirectory(_directory);
        File.WriteAllText(store.PlanPath, "{ not json");

        IOException refusal = Assert.Throws<IOException>(() => store.ReadPlan());

        Assert.Contains(store.PlanPath, refusal.Message, StringComparison.Ordinal);
        Assert.Contains("Delete it", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_plan_of_another_format_is_refused()
    {
        RunPlanStore store = Store();

        store.WritePlan(Plan() with { Format = RunPlan.CurrentFormat + 1 });

        IOException refusal = Assert.Throws<IOException>(() => store.ReadPlan());

        Assert.Contains("format", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Deleting_the_plan_leaves_the_other_files_alone()
    {
        RunPlanStore store = Store();

        store.WritePlan(Plan());
        File.WriteAllText(store.LogPath, "{}\n");

        store.DeletePlan();
        store.DeletePlan();

        Assert.False(File.Exists(store.PlanPath));
        Assert.True(File.Exists(store.LogPath));
    }
}
