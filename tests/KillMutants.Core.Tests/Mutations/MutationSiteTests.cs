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

    /// <summary>
    /// Closes RB-016, found by running KillMutants on its own source. A pattern or `out` variable is
    /// definitely assigned only conditionally, and every mutation this tool makes to the expression
    /// that declares it changes when its parts are evaluated. `x is not T t || f(t)` turned into
    /// `x is not T t &amp;&amp; f(t)` leaves `t` unassigned; so does swapping the branches of
    /// `d.TryGetValue(k, out var v) ? v : 0`. Neither compiles, so neither is proposed.
    /// </summary>
    [Theory]
    // a pattern variable read after the guard: `||` into `&&` orphans it
    [InlineData("class C { int M(object o) { if (o is not string s || s.Length >= 3) { return 0; } return s.Length; } }",
                "s.Length >= 3")]
    // an `out` variable, the other half of the same problem
    [InlineData("class C { int M(System.Collections.Generic.Dictionary<int,int> d) " +
                "{ if (!d.TryGetValue(1, out int v) || v >= 3) { return 0; } return v; } }",
                "v >= 3")]
    public void An_expression_that_declares_a_variable_is_not_mutated(string source, string stillMutated)
    {
        IReadOnlyList<Mutant> mutants = Generate(source);

        // The declaring expression yields nothing...
        Assert.DoesNotContain(mutants, mutant => mutant.OriginalText.Contains("is not", StringComparison.Ordinal));
        Assert.DoesNotContain(mutants, mutant => mutant.OriginalText.Contains("TryGetValue", StringComparison.Ordinal));

        // ...while the ordinary expressions beside it still do. The rule must not swallow the file.
        Assert.Contains(mutants, mutant => mutant.OriginalText == stillMutated);
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
