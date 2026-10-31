using System.Text.Json;
using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;
using KillMutants.Reporting;

namespace KillMutants.Core.Tests.Reporting;

/// <summary>
/// How a run that set part of its scope aside reports it (DEC0022): the parts and their reasons
/// first, a score labelled with the part it covers - or none when a project could not be read -
/// and the same in the JSON a CI job reads.
/// </summary>
public class SetAsideReportTests
{
    private static readonly SetAside RedBaseline =
        new(SetAsideStage.Baseline, "Core", "'Core.Tests' does not pass against unmutated code.\nCore.Tests.T.Fails");

    private static readonly SetAside Unreadable =
        new(SetAsideStage.Discovery, "tools/probe/Probe.csproj", "MSBuild failed for 'Probe.csproj'.");

    private static MutantResult[] Results()
    {
        IReadOnlyList<Mutant> mutants = new MutantGenerator(MutatorCatalog.Default)
            .Generate(TestCompilation.From("class C { bool M(int a) => a >= 18; }", "/src/Ages.cs"));

        return [new MutantResult(mutants[0], MutantStatus.Killed), new MutantResult(mutants[1], MutantStatus.Survived)];
    }

    private static string Console(MutationTestReport report)
    {
        var writer = new StringWriter();
        ConsoleReportWriter.Write(writer, report);

        return writer.ToString().ReplaceLineEndings("\n");
    }

    private static JsonDocument Json(MutationTestReport report)
    {
        var writer = new StringWriter();
        JsonReportWriter.Write(writer, report);

        return JsonDocument.Parse(writer.ToString());
    }

    [Fact]
    public void A_part_set_aside_after_discovery_is_named_first_and_the_score_says_what_it_covers()
    {
        var report = new MutationTestReport(Results(), setAside: [RedBaseline]);
        string output = Console(report);

        Assert.True(report.HasScore);
        Assert.False(report.MeasuredEverything);
        Assert.True(
            output.IndexOf("Set aside (1)", StringComparison.Ordinal) <
            output.IndexOf("Survived (1)", StringComparison.Ordinal));
        Assert.Contains("  Core (red baseline)", output, StringComparison.Ordinal);
        Assert.Contains("    Core.Tests.T.Fails", output, StringComparison.Ordinal);
        Assert.Contains("Mutation score: 50%, over the part measured only", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_project_set_aside_at_discovery_withholds_the_score()
    {
        var report = new MutationTestReport(Results(), setAside: [Unreadable]);
        string output = Console(report);

        Assert.False(report.HasScore);
        Assert.Contains("tools/probe/Probe.csproj (could not be read)", output, StringComparison.Ordinal);
        Assert.Contains("No mutation score: a project set aside at discovery", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Mutation score: 50%", output, StringComparison.Ordinal);
        Assert.DoesNotContain("(50%)", output, StringComparison.Ordinal);
    }

    [Fact]
    public void The_json_lists_what_was_set_aside_and_nulls_a_score_it_cannot_ground()
    {
        using JsonDocument labelled = Json(new MutationTestReport(Results(), setAside: [RedBaseline]));
        using JsonDocument withheld = Json(new MutationTestReport(Results(), setAside: [Unreadable]));

        JsonElement part = Assert.Single(labelled.RootElement.GetProperty("setAside").EnumerateArray());
        Assert.Equal("Baseline", part.GetProperty("stage").GetString());
        Assert.Equal("Core", part.GetProperty("name").GetString());
        Assert.Equal(0.5, labelled.RootElement.GetProperty("score").GetDouble());

        Assert.Equal(JsonValueKind.Null, withheld.RootElement.GetProperty("score").ValueKind);
    }

    /// <summary>
    /// A target set aside at its baseline keeps its mutants selected and gives them no verdict, in a
    /// run that was not stopped: the JSON says the run did not judge everything, and counts them.
    /// </summary>
    [Fact]
    public void The_json_counts_the_mutants_a_set_aside_target_left_unjudged()
    {
        using JsonDocument json = Json(new MutationTestReport(Results(), setAside: [RedBaseline], selected: 5));

        Assert.False(json.RootElement.GetProperty("complete").GetBoolean());
        Assert.Equal(5, json.RootElement.GetProperty("mutantsSelected").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("totals").GetProperty("mutants").GetInt32());
    }

    [Fact]
    public void A_run_that_measured_everything_reports_as_it_always_has()
    {
        var report = new MutationTestReport(Results());
        string output = Console(report);

        Assert.True(report.MeasuredEverything);
        Assert.DoesNotContain("Set aside", output, StringComparison.Ordinal);
        Assert.Contains("Mutation score: 50%\n", output, StringComparison.Ordinal);

        using JsonDocument json = Json(report);
        Assert.Empty(json.RootElement.GetProperty("setAside").EnumerateArray());
        Assert.True(json.RootElement.GetProperty("complete").GetBoolean());
        Assert.Equal(2, json.RootElement.GetProperty("mutantsSelected").GetInt32());
    }
}
