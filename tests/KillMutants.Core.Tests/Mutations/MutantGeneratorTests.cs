using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace KillMutants.Core.Tests.Mutations;

public class MutantGeneratorTests
{
    private static IReadOnlyList<Mutant> Generate(string source, string path = "/src/Sample.cs")
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source, path: path);

        return new MutantGenerator(MutatorCatalog.Default).Generate([tree]);
    }

    [Fact]
    public void A_single_comparison_yields_a_single_mutant()
    {
        IReadOnlyList<Mutant> mutants = Generate(
            "static class Ages { public static bool IsAdult(int age) => age >= 18; }");

        Mutant mutant = Assert.Single(mutants);
        Assert.Equal("age >= 18", mutant.OriginalText);
        Assert.Equal("age > 18", mutant.MutatedText);
        Assert.Equal("GreaterThanOrEqual", mutant.Mutator.ToString());
    }

    [Fact]
    public void Mutants_are_numbered_from_one()
    {
        IReadOnlyList<Mutant> mutants = Generate(
            "class C { bool A(int x) => x >= 1; bool B(int x) => x >= 2; }");

        Assert.Equal(2, mutants.Count);
        Assert.Equal("M1", mutants[0].Id.ToString());
        Assert.Equal("M2", mutants[1].Id.ToString());
    }

    [Fact]
    public void A_mutant_reports_where_it_is_in_the_source()
    {
        IReadOnlyList<Mutant> mutants = Generate(
            "class C\n{\n    bool M(int age) => age >= 18;\n}",
            path: "/src/Ages.cs");

        Mutant mutant = Assert.Single(mutants);
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
