using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;

namespace KillMutants.Core.Tests.Mutations;

/// <summary>
/// `[ExcludeFromCodeCoverage]` is a statement of intent: this code is not part of what the tests are
/// expected to cover. Mutating it produces survivors the developer has already said they do not want
/// counted - and since uncovered and surviving mutants both weigh on the score, those findings do
/// not merely clutter the report, they move the number.
/// </summary>
public class ExcludedFromCoverageTests
{
    private const string Attribute = "using System.Diagnostics.CodeAnalysis;\n";

    private static IReadOnlyList<Mutant> Generate(string source) =>
        new MutantGenerator(MutatorCatalog.Default).Generate(TestCompilation.From(Attribute + source));

    [Theory]
    // on the method itself
    [InlineData("class C { [ExcludeFromCodeCoverage] public bool M(int a) => a >= 18; }")]
    // on the type, so everything in it
    [InlineData("[ExcludeFromCodeCoverage] class C { public bool M(int a) => a >= 18; }")]
    // spelled out in full, which is the same attribute
    [InlineData("class C { [ExcludeFromCodeCoverageAttribute] public bool M(int a) => a >= 18; }")]
    // sharing a bracketed list with another attribute
    [InlineData("class C { [Obsolete, ExcludeFromCodeCoverage] public bool M(int a) => a >= 18; }")]
    // on one accessor of a property
    [InlineData("class C { public bool M { [ExcludeFromCodeCoverage] get => 1 >= 0; } }")]
    public void Code_marked_as_not_measured_is_not_mutated(string source)
    {
        Assert.Empty(Generate(source));
    }

    [Fact]
    public void The_rule_stops_at_what_is_marked()
    {
        IReadOnlyList<Mutant> mutants = Generate(
            "class C { [ExcludeFromCodeCoverage] public bool A(int a) => a >= 18; " +
            "public bool B(int a) => a >= 21; }");

        Assert.NotEmpty(mutants);
        Assert.All(mutants, mutant => Assert.Equal("a >= 21", mutant.OriginalText));
    }

    /// <summary>
    /// Resolved through the semantic model, not by matching the name, so a same-named attribute of
    /// the user's own cannot silence a class by accident.
    /// </summary>
    [Fact]
    public void An_unrelated_attribute_of_the_same_name_does_not_exclude_anything()
    {
        IReadOnlyList<Mutant> mutants = new MutantGenerator(MutatorCatalog.Default).Generate(
            TestCompilation.From(
                "namespace Mine { public sealed class ExcludeFromCodeCoverageAttribute : System.Attribute { } " +
                "class C { [ExcludeFromCodeCoverage] public bool M(int a) => a >= 18; } }"));

        Assert.NotEmpty(mutants);
    }
}
