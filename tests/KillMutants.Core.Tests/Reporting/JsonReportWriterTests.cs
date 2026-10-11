using System.Text.Json;
using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;
using KillMutants.Reporting;
using Microsoft.CodeAnalysis;

namespace KillMutants.Core.Tests.Reporting;

/// <summary>
/// The JSON report is what CI, a dashboard or a diff between two runs will read, so its shape is
/// asserted rather than left to chance.
/// </summary>
public class JsonReportWriterTests
{
    private static JsonDocument Render(params MutantResult[] results)
    {
        var writer = new StringWriter();

        JsonReportWriter.Write(writer, new MutationTestReport(results, TimeSpan.FromSeconds(3.5)));

        return JsonDocument.Parse(writer.ToString());
    }

    private static IReadOnlyList<Mutant> MutantsFor(string source) =>
        new MutantGenerator(MutatorCatalog.Default)
            .Generate(TestCompilation.From(source, "/src/Ages.cs"));

    [Fact]
    public void Every_mutant_is_named_with_its_identity_position_and_outcome()
    {
        Mutant mutant = MutantsFor("class C { bool M(int a) => a >= 18; }")[0];

        using JsonDocument json = Render(new MutantResult(mutant, MutantStatus.Survived));
        JsonElement first = json.RootElement.GetProperty("mutants")[0];

        Assert.Equal("M1", first.GetProperty("id").GetString());
        Assert.Equal("Comparison", first.GetProperty("mutator").GetString());
        Assert.Equal("Survived", first.GetProperty("status").GetString());
        Assert.Equal("/src/Ages.cs", first.GetProperty("file").GetString());
        Assert.Equal(1, first.GetProperty("line").GetInt32());
        Assert.Equal("a >= 18", first.GetProperty("original").GetString());
        Assert.Equal("a > 18", first.GetProperty("mutated").GetString());
    }

    [Fact]
    public void The_totals_and_the_score_are_reported_as_numbers()
    {
        IReadOnlyList<Mutant> mutants = MutantsFor("class C { bool M(int a) => a >= 18; }");

        using JsonDocument json = Render(
            new MutantResult(mutants[0], MutantStatus.Killed),
            new MutantResult(mutants[1], MutantStatus.Survived));

        Assert.Equal(0.5, json.RootElement.GetProperty("score").GetDouble());
        Assert.Equal("50%", json.RootElement.GetProperty("scoreDisplay").GetString());
        Assert.Equal(3.5, json.RootElement.GetProperty("durationSeconds").GetDouble());

        JsonElement totals = json.RootElement.GetProperty("totals");
        Assert.Equal(2, totals.GetProperty("mutants").GetInt32());
        Assert.Equal(1, totals.GetProperty("killed").GetInt32());
        Assert.Equal(1, totals.GetProperty("survived").GetInt32());
    }

    [Fact]
    public void An_undefined_score_is_null_rather_than_a_zero_that_was_never_measured()
    {
        // JSON has no NaN, and reporting 0 would claim the tests caught nothing when in fact
        // nothing was testable.
        using JsonDocument json = Render();

        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("score").ValueKind);
        Assert.Equal("n/a", json.RootElement.GetProperty("scoreDisplay").GetString());
    }

    [Fact]
    public void Operators_are_left_unescaped_so_the_report_can_be_searched()
    {
        Mutant mutant = MutantsFor("class C { bool M(int a) => a >= 18; }")[0];
        var writer = new StringWriter();

        JsonReportWriter.Write(writer, new MutationTestReport([new MutantResult(mutant, MutantStatus.Killed)]));

        // Grepping a mutation report for ">=" must find it; the default encoder would write \u003E.
        Assert.Contains("a >= 18", writer.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("u003E", writer.ToString(), StringComparison.Ordinal);
    }

    private static JsonDocument RenderStopped(int? selected, params MutantResult[] results)
    {
        var writer = new StringWriter();

        JsonReportWriter.Write(
            writer, new MutationTestReport(results, stopped: new EarlyStop(selected)));

        return JsonDocument.Parse(writer.ToString());
    }

    [Fact]
    public void A_complete_run_says_so_and_selected_what_it_judged()
    {
        IReadOnlyList<Mutant> mutants = MutantsFor("class C { bool M(int a) => a >= 18; }");

        using JsonDocument json = Render(
            new MutantResult(mutants[0], MutantStatus.Killed),
            new MutantResult(mutants[1], MutantStatus.Survived));

        Assert.True(json.RootElement.GetProperty("complete").GetBoolean());
        Assert.Equal(2, json.RootElement.GetProperty("mutantsSelected").GetInt32());
    }

    /// <summary>
    /// A stopped run judged the mutants it reached first, which are not a sample of anything, so it
    /// carries no score - not the run's, and not a family's (DEC0013). The counts stay.
    /// </summary>
    [Fact]
    public void A_stopped_run_carries_its_counts_and_no_score()
    {
        IReadOnlyList<Mutant> mutants = MutantsFor("class C { bool M(int a) => a >= 18; }");

        using JsonDocument json = RenderStopped(
            selected: 9,
            new MutantResult(mutants[0], MutantStatus.Killed),
            new MutantResult(mutants[1], MutantStatus.Survived));

        JsonElement root = json.RootElement;

        Assert.False(root.GetProperty("complete").GetBoolean());
        Assert.Equal(9, root.GetProperty("mutantsSelected").GetInt32());
        Assert.Equal(2, root.GetProperty("totals").GetProperty("mutants").GetInt32());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("score").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("scoreDisplay").ValueKind);
        Assert.All(
            root.GetProperty("byMutator").EnumerateArray(),
            family => Assert.Equal(JsonValueKind.Null, family.GetProperty("score").ValueKind));
    }

    [Fact]
    public void A_run_stopped_before_it_selected_anything_says_it_does_not_know_how_many()
    {
        using JsonDocument json = RenderStopped(selected: null);

        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("mutantsSelected").ValueKind);
    }

    /// <summary>Zero changed files would say nothing changed, which nothing established.</summary>
    [Fact]
    public void A_change_the_run_never_read_has_no_file_count()
    {
        var writer = new StringWriter();

        JsonReportWriter.Write(
            writer,
            new MutationTestReport([], scope: RunScope.Unresolved, stopped: new EarlyStop(null)));

        using JsonDocument json = JsonDocument.Parse(writer.ToString());
        JsonElement scope = json.RootElement.GetProperty("scope");

        Assert.Equal("partial", scope.GetProperty("mode").GetString());
        Assert.Equal(JsonValueKind.Null, scope.GetProperty("changedFiles").ValueKind);
    }

    /// <summary>
    /// A line of the verdict log is an entry of <c>mutants</c>, on one line, so a consumer reading
    /// the log a cancelled run left reads it with the code it already has for the report.
    /// </summary>
    [Fact]
    public void A_verdict_line_is_a_mutants_entry_on_one_line()
    {
        Mutant mutant = MutantsFor("class C { bool M(int a) => a >= 18; }")[0];
        var result = new MutantResult(mutant, MutantStatus.Survived);

        string line = JsonReportWriter.Line(result);

        using JsonDocument json = Render(result);
        using JsonDocument parsed = JsonDocument.Parse(line);

        Assert.DoesNotContain('\n', line);
        Assert.True(JsonElement.DeepEquals(json.RootElement.GetProperty("mutants")[0], parsed.RootElement));
    }
}
