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
            .Generate([CSharpSyntaxTree.ParseText(source, path: "/src/Ages.cs")]);

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

            Survived:
              Ages.cs(1,28)  a >= 18 -> a > 18  [Comparison]

            Mutants: 2
            Killed: 1
            Survived: 1

            Mutation score: 50%

            """.ReplaceLineEndings("\n"),
            output);
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
