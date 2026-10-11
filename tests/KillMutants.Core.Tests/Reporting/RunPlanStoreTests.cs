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
        [new PlannedRun("2026-10-11T20:00:00.0000000+00:00", "0.1.0-alpha", ".NET 10", "Linux", 4, 2, 0, 1, 0,
            [new PlannedDisagreement("7223f8dbce52736a", "reported Killed and then Survived")])]);

    [Theory]
    [InlineData("report.json", "report.plan.json")]
    [InlineData("mutation", "mutation.plan.json")]
    [InlineData("out/mutation.JSON", "out/mutation.plan.json")]
    public void The_plan_takes_the_reports_name_with_its_own_extension(string report, string expected)
    {
        Assert.Equal(expected, RunPlanStore.PlanPathBeside(report));
    }

    /// <summary>
    /// The pending copy is among the files to leave alone: a run killed mid-write leaves it behind,
    /// and counted as an edit of the tree it would keep every later run from continuing the series.
    /// </summary>
    [Fact]
    public void The_files_the_run_writes_share_a_name_and_include_the_pending_plan()
    {
        RunPlanStore store = Store();

        Assert.Equal(
            [store.ReportPath, store.LogPath, store.PlanPath, store.PlanPath + ".tmp"],
            store.Files);
        Assert.EndsWith("report.jsonl", store.LogPath, StringComparison.Ordinal);
        Assert.EndsWith("report.plan.json", store.PlanPath, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pending_copy_a_killed_run_left_goes_with_the_plan()
    {
        RunPlanStore store = Store();

        store.WritePlan(Plan());
        File.WriteAllText(store.PlanPath + ".tmp", "{ half");

        store.DeletePlan();

        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public void A_plan_missing_part_of_itself_is_refused()
    {
        RunPlanStore store = Store();

        Directory.CreateDirectory(_directory);
        File.WriteAllText(store.PlanPath, """{ "format": 1, "series": "nightly" }""");

        IOException refusal = Assert.Throws<IOException>(() => store.ReadPlan());

        Assert.Contains("missing part", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The serializer fills a missing list with null rather than with nothing, and the first use of
    /// one would be an unnamed crash after the log was opened. Each part the continuation reads is
    /// checked, and the file is named.
    /// </summary>
    [Theory]
    [InlineData("\"mutators\": [\"Comparison\"],", "")]
    [InlineData("\"exclude\": [],", "")]
    [InlineData("\"disagreements\": []", "\"other\": []")]
    [InlineData("\"key\": \"7223f8dbce52736a\", \"id\"", "\"id\"")]
    public void A_plan_missing_a_nested_part_is_refused(string present, string absent)
    {
        RunPlanStore store = Store();
        const string whole = """
            {
              "format": 1,
              "series": "nightly",
              "identity": {
                "code": { "name": "0123abcd", "isExact": true, "directory": "" },
                "toolVersion": "0.1.0-alpha",
                "configuration": "Release",
                "coverage": true,
                "mutators": ["Comparison"],
                "without": [],
                "exclude": [],
                "scope": null,
                "fingerprint": null,
                "targetGraph": null,
                "testFramework": null
              },
              "mutants": [
                { "key": "7223f8dbce52736a", "id": "M1", "mutator": "Comparison", "path": "src/Ages.cs",
                  "line": 3, "character": 17, "original": "a >= 18", "mutated": "a > 18" }
              ],
              "runs": [
                { "started": "2026-10-11T20:00:00.0000000+00:00", "toolVersion": "0.1.0-alpha",
                  "runtime": ".NET 10", "operatingSystem": "Linux", "processorCount": 4, "workerCount": 2,
                  "logLinesBefore": 0, "verdicts": 1, "killsReVerified": 0, "disagreements": [] }
              ]
            }
            """;

        Directory.CreateDirectory(_directory);
        File.WriteAllText(store.PlanPath, whole);

        Assert.NotNull(store.ReadPlan());

        Assert.Contains(present, whole, StringComparison.Ordinal);
        File.WriteAllText(store.PlanPath, whole.Replace(present, absent, StringComparison.Ordinal));

        IOException refusal = Assert.Throws<IOException>(() => store.ReadPlan());

        Assert.Contains("missing part", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(store.PlanPath, refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The first plan of a series is written before anything is discovered, so a run stopped during
    /// the build leaves one; it has no mutants yet, and says so rather than listing none.
    /// </summary>
    [Fact]
    public void A_plan_written_before_the_mutants_are_selected_has_none_and_is_read_back()
    {
        RunPlanStore store = Store();

        store.WritePlan(Plan() with { Mutants = null });

        RunPlan read = Assert.IsType<RunPlan>(store.ReadPlan());

        Assert.Null(read.Mutants);
        Assert.Contains("\"mutants\": null", File.ReadAllText(store.PlanPath), StringComparison.Ordinal);
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
        Assert.Equal(written.Mutants!, read.Mutants!);
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
