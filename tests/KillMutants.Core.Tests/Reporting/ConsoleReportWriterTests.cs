using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;
using KillMutants.Reporting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace KillMutants.Core.Tests.Reporting;

/// <summary>
/// Rendering is tested against constructed reports rather than a real run, so that growing the
/// mutator catalogue cannot silently change what these assertions mean.
/// </summary>
public class ConsoleReportWriterTests
{
    private static IReadOnlyList<Mutant> MutantsFor(string source) =>
        new MutantGenerator(MutatorCatalog.Default)
            .Generate(TestCompilation.From(source, "/src/Ages.cs"));

    private static string Render(params MutantResult[] results)
    {
        var writer = new StringWriter();

        ConsoleReportWriter.Write(writer, new MutationTestReport(results));

        return writer.ToString().ReplaceLineEndings("\n");
    }

    [Fact]
    public void A_run_where_every_mutant_died_renders_the_headline_figures()
    {
        Mutant mutant = MutantsFor("class C { bool M(int a) => a >= 18; }")[0];

        Assert.Equal(
            """
            KillMutants

            Mutants: 1
            Killed: 1
            Survived: 0

            Mutation score: 100%

            """.ReplaceLineEndings("\n"),
            Render(new MutantResult(mutant, MutantStatus.Killed)));
    }

    [Fact]
    public void Survivors_are_named_before_the_totals_because_they_are_the_point_of_the_run()
    {
        IReadOnlyList<Mutant> mutants = MutantsFor("class C { bool M(int a) => a >= 18; }");

        string output = Render(
            new MutantResult(mutants[0], MutantStatus.Survived),
            new MutantResult(mutants[1], MutantStatus.Killed));

        Assert.Equal(
            """
            KillMutants

            Survived (1)

              Ages.cs
                1:28  a >= 18 -> a > 18  [Comparison]

            Mutants: 2
            Killed: 1
            Survived: 1

            Mutation score: 50%

            """.ReplaceLineEndings("\n"),
            output);
    }

    [Fact]
    public void Findings_are_grouped_by_file_and_lined_up()
    {
        // Two files, several mutants each, deliberately out of order.
        IReadOnlyList<Mutant> mutants = MutantsFor(
            "class C { bool M(int a) => a >= 18; bool N(int a, bool b) => b && a >= 5; }");

        string output = Render([.. mutants.Select(m => new MutantResult(m, MutantStatus.Survived))]);

        Assert.Contains("Survived (5)", output, StringComparison.Ordinal);
        Assert.Contains("  Ages.cs", output, StringComparison.Ordinal);
        Assert.Contains("[LogicalOperator]", output, StringComparison.Ordinal);

        // The point of the padding: every arrow lands in the same column, so a block of findings
        // reads as a table rather than as ragged text.
        int[] arrowColumns = [.. output
            .Split('\n')
            .Where(line => line.Contains(" -> ", StringComparison.Ordinal))
            .Select(line => line.IndexOf(" -> ", StringComparison.Ordinal))];

        Assert.Equal(5, arrowColumns.Length);
        Assert.Single(arrowColumns.Distinct());
    }

    [Fact]
    public void Uncovered_mutants_are_named_too_because_untested_code_is_its_own_finding()
    {
        Mutant mutant = MutantsFor("class C { bool M(int a) => a >= 18; }")[0];

        string output = Render(new MutantResult(mutant, MutantStatus.NoCoverage));

        Assert.Contains("No coverage (1)", output, StringComparison.Ordinal);
        Assert.Contains("Ages.cs", output, StringComparison.Ordinal);
    }

    [Fact]
    public void The_elapsed_time_is_shown_when_it_was_measured()
    {
        Mutant mutant = MutantsFor("class C { bool M(int a) => a >= 18; }")[0];
        var writer = new StringWriter();

        ConsoleReportWriter.Write(
            writer,
            new MutationTestReport([new MutantResult(mutant, MutantStatus.Killed)], TimeSpan.FromSeconds(12.34)));

        Assert.Contains("Elapsed: 12.3 s", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Timeouts_and_compile_errors_are_only_shown_when_they_happened()
    {
        IReadOnlyList<Mutant> mutants = MutantsFor("class C { bool M(int a) => a >= 18; }");

        string output = Render(
            new MutantResult(mutants[0], MutantStatus.Timeout),
            new MutantResult(mutants[1], MutantStatus.CompileError, "CS0162"));

        Assert.Contains("Timed out: 1", output, StringComparison.Ordinal);
        Assert.Contains("Compile errors: 1", output, StringComparison.Ordinal);

        // A mutant that could not be tested is excluded from the score rather than counted against
        // the suite; a timeout is a detection.
        Assert.Contains("Mutation score: 100%", output, StringComparison.Ordinal);
    }
}
