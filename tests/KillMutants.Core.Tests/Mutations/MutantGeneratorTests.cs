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
    private static IReadOnlyList<Mutant> GenerateWithNumericLiterals(string source) =>
        new MutantGenerator(MutatorCatalog.Of(added: [MutatorName.Create("NumericLiteral")]))
            .Generate(TestCompilation.From(source));

    private static string[] NumericMutants(string source) =>
        [.. GenerateWithNumericLiterals(source)
            .Where(mutant => mutant.Mutator.ToString() == "NumericLiteral")
            .Select(mutant => $"{mutant.OriginalText} -> {mutant.MutatedText}")];

    /// <summary>
    /// The project under test is recompiled with the mutant, so its own read of the constant sees
    /// the new value even though the test assembly keeps the old one. DEC0019.
    /// </summary>
    [Fact]
    public void A_constant_its_project_reads_is_moved()
    {
        Assert.Equal(
            ["100 -> 101", "100 -> 99"],
            NumericMutants(
                "public class Themes { public const int MinimumNouns = 100; " +
                "public static bool IsValid(int nouns) => nouns >= MinimumNouns; }"));
    }

    /// <summary>
    /// Only the tests could read it, and they keep the value they were compiled against: the mutant
    /// would survive whatever they assert, a gap that does not exist.
    /// </summary>
    [Fact]
    public void A_constant_its_project_never_reads_is_left_alone()
    {
        Assert.Empty(NumericMutants("public class Themes { public const int MinimumNouns = 100; }"));
    }

    /// <summary>
    /// A read that is itself copied onward - into another constant, an attribute, a parameter's
    /// default - does not count, since the copy is where the trail goes cold.
    /// </summary>
    [Theory]
    [InlineData("public class C { public const int A = 100; public const int B = A + 1; }")]
    [InlineData("public class C { public const int A = 100; public static int M(int x = A) => x; }")]
    [InlineData("public class C { public const int A = 100; [System.ComponentModel.DefaultValue(A)] public int P { get; set; } }")]
    public void A_constant_read_only_where_it_is_copied_onward_is_left_alone(string source)
    {
        Assert.DoesNotContain(NumericMutants(source), mutant => mutant.StartsWith("100 ", StringComparison.Ordinal));
    }

    /// <summary><c>nameof</c> reads the name, never the value, so it is no read at all.</summary>
    [Fact]
    public void A_constant_named_only_by_nameof_is_left_alone()
    {
        Assert.Empty(NumericMutants(
            "public class C { public const int Limit = 100; public static string Name() => nameof(Limit); }"));
    }

    [Fact]
    public void A_constant_read_from_another_file_of_the_project_counts()
    {
        Compilation compilation = TestCompilation.From("public class Themes { public const int MinimumNouns = 100; }")
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(
                "public class Validator { public bool IsValid(int n) => n >= Themes.MinimumNouns; }",
                path: "/src/Validator.cs",
                cancellationToken: TestContext.Current.CancellationToken));

        IReadOnlyList<Mutant> mutants = new MutantGenerator(
                MutatorCatalog.Of(added: [MutatorName.Create("NumericLiteral")]))
            .Generate(compilation, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            ["101", "99"],
            mutants.Where(mutant => mutant.Mutator.ToString() == "NumericLiteral").Select(mutant => mutant.MutatedText));
    }

    /// <summary>The exception is the opt-in family's alone: the default catalog still never reaches a constant.</summary>
    [Fact]
    public void Other_families_still_leave_a_read_constant_alone()
    {
        Assert.DoesNotContain(
            GenerateWithNumericLiterals(
                "public class C { public const bool Flag = 3 >= 2; public static bool M() => Flag; }"),
            mutant => mutant.Mutator.ToString() != "NumericLiteral");
    }
}
