using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace KillMutants.Core.Tests.Mutations;

public class MutantGeneratorTests
{
    private static IReadOnlyList<Mutant> Generate(string source, string path = "/src/Sample.cs") =>
        new MutantGenerator(MutatorCatalog.Default).Generate(TestCompilation.From(source, path));

    [Fact]
    public void A_comparison_yields_its_boundary_shift_and_its_negation()
    {
        IReadOnlyList<Mutant> mutants = Generate(
            "static class Ages { public static bool IsAdult(int age) => age >= 18; }");

        Assert.All(mutants, mutant => Assert.Equal("age >= 18", mutant.OriginalText));
        Assert.All(mutants, mutant => Assert.Equal("Comparison", mutant.Mutator.ToString()));
        Assert.Equal(["age > 18", "age < 18"], mutants.Select(mutant => mutant.MutatedText));
    }

    [Fact]
    public void Mutants_are_numbered_from_one_across_the_whole_run()
    {
        IReadOnlyList<Mutant> mutants = Generate(
            "class C { bool A(int x) => x >= 1; bool B(int x) => x >= 2; }");

        Assert.Equal(["M1", "M2", "M3", "M4"], mutants.Select(mutant => mutant.Id.ToString()));
    }

    [Fact]
    public void Numbering_continues_across_calls_so_one_run_never_repeats_an_identifier()
    {
        // M3 will generate per project. Restarting at M1 for each would make the report ambiguous.
        var generator = new MutantGenerator(MutatorCatalog.Default);
        Compilation Snippet() => TestCompilation.From("class C { bool M(int x) => x >= 1; }");

        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        IReadOnlyList<Mutant> first =
            generator.Generate(Snippet(), cancellationToken: cancellationToken);
        IReadOnlyList<Mutant> second =
            generator.Generate(Snippet(), cancellationToken: cancellationToken);

        Assert.Equal(["M1", "M2"], first.Select(mutant => mutant.Id.ToString()));
        Assert.Equal(["M3", "M4"], second.Select(mutant => mutant.Id.ToString()));
    }

    /// <summary>
    /// Every project's mutants are generated before any is tested, so a stop that generation did
    /// not observe would wait for all of them - on a large repository, the margin a deadline leaves
    /// before the CI's own kill (DEC0013).
    /// </summary>
    [Fact]
    public void Generation_stops_when_the_run_is_stopped()
    {
        using var stop = new CancellationTokenSource();

        stop.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(
            () => new MutantGenerator(MutatorCatalog.Default).Generate(
                TestCompilation.From("class C { bool M(int x) => x >= 1; }"),
                cancellationToken: stop.Token));
    }

    [Fact]
    public void A_mutant_reports_where_it_is_in_the_source()
    {
        IReadOnlyList<Mutant> mutants = Generate(
            "class C\n{\n    bool M(int age) => age >= 18;\n}",
            path: "/src/Ages.cs");

        Mutant mutant = mutants[0];
        Assert.Equal("/src/Ages.cs", mutant.Location.FilePath);
        Assert.Equal(3, mutant.Location.Line);
        Assert.Equal(24, mutant.Location.Character);
        Assert.Equal("Ages.cs(3,24)", mutant.Location.ToString());
    }

    [Fact]
    public void Code_without_a_mutable_expression_yields_nothing()
    {
        Assert.Empty(Generate("class C { int M() => 42; }"));
    }

    [Theory]
    [InlineData("/proj/obj/Debug/net10.0/Sample.AssemblyInfo.cs")]
    [InlineData("/proj/Sample.GlobalUsings.g.cs")]
    public void Generated_sources_are_not_mutated(string path)
    {
        // Generated files are compiler inputs, not the developer's code: a finding there
        // could not be acted on. They are still compiled, just never mutated.
        Assert.Empty(Generate("class C { bool M(int x) => x >= 1; }", path));
    }
}
