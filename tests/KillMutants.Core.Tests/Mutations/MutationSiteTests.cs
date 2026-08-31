using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace KillMutants.Core.Tests.Mutations;

/// <summary>
/// C# copies compile-time constants into every call site. Mutating one changes the assembly under
/// test but not the already-compiled test assembly that reads it, so no test can possibly notice.
/// Such a mutant is guaranteed to survive however good the tests are, and reporting it would invent
/// a gap the user cannot act on.
/// </summary>
public class MutationSiteTests
{
    private static IReadOnlyList<Mutant> Generate(string source) =>
        new MutantGenerator(MutatorCatalog.Default).Generate(TestCompilation.From(source));

    [Theory]
    // const field: the value is inlined into every consumer at their build time
    [InlineData("class C { const bool Flag = 3 >= 2; }")]
    // const local: same rule inside a method body
    [InlineData("class C { void M() { const bool Flag = 3 >= 2; } }")]
    // default parameter value: copied into each call site
    [InlineData("class C { void M(bool flag = 3 >= 2) { } }")]
    // attribute argument: baked into metadata
    [InlineData("class C { [System.Obsolete(nameof(C) + \"\", 3 >= 2)] void M() { } }")]
    // enum member: a compile-time constant
    [InlineData("enum E { A = 3 >= 2 ? 1 : 0 }")]
    public void A_mutation_that_could_never_be_observed_is_not_generated(string source)
    {
        Assert.Empty(Generate(source));
    }

    [Theory]
    // an ordinary expression-bodied member
    [InlineData("class C { bool M(int a) => a >= 2; }")]
    // a non-const field initialiser is evaluated at run time, so it IS observable
    [InlineData("class C { bool Flag = 3 >= 2; }")]
    // a static readonly field is also a run-time evaluation, unlike const
    [InlineData("class C { static readonly bool Flag = 3 >= 2; }")]
    // an ordinary local
    [InlineData("class C { void M() { var flag = 3 >= 2; } }")]
    public void An_observable_mutation_is_still_generated(string source)
    {
        Assert.NotEmpty(Generate(source));
    }

    [Fact]
    public void A_method_beside_a_constant_is_still_mutated()
    {
        // The constant must not shadow the rest of the type.
        IReadOnlyList<Mutant> mutants = Generate(
            "class C { const bool Flag = 3 >= 2; bool M(int a) => a >= 2; }");

        Assert.NotEmpty(mutants);
        Assert.All(mutants, mutant => Assert.Equal("a >= 2", mutant.OriginalText));
    }
}
