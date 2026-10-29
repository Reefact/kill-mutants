using Microsoft.CodeAnalysis;
using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;
using Microsoft.CodeAnalysis.CSharp;

namespace KillMutants.Core.Tests.Mutations.Mutators;

public class ConditionalExpressionMutatorTests
{
    private static readonly ConditionalExpressionMutator Mutator = new();

    /// <summary>
    /// Regression test (DEC0017). The condition is forced each way, one mutant per branch, and
    /// still evaluated: the forced value is written beside it, not in its place.
    /// </summary>
    [Fact]
    public void The_condition_is_forced_each_way()
    {
        Assert.Equal(
            ["(a || true) ? x : y", "(a && false) ? x : y"],
            MutatorTestBase.MutatedTexts(Mutator, "a ? x : y"));
    }

    /// <summary>
    /// The branch swap this family made before would die exactly when one of the forced mutants does,
    /// so it is no longer made.
    /// </summary>
    [Fact]
    public void The_branches_are_no_longer_swapped()
    {
        Assert.DoesNotContain("a ? y : x", MutatorTestBase.MutatedTexts(Mutator, "a ? x : y"));
    }

    /// <summary>
    /// A call in the condition still runs, which is what keeps its side effects: Stryker.NET's
    /// <c>true ? a : b</c> would drop it.
    /// </summary>
    [Fact]
    public void A_condition_that_calls_is_still_evaluated()
    {
        IReadOnlyList<string> mutated = MutatorTestBase.MutatedTexts(
            Mutator, "System.Func<bool> f, int x, int y", "int M({0}) => f() ? x : y;");

        Assert.Equal(["(f() || true) ? x : y", "(f() && false) ? x : y"], mutated);
    }

    /// <summary>A compound condition is forced whole, not in its last operand.</summary>
    [Fact]
    public void A_compound_condition_is_forced_whole()
    {
        IReadOnlyList<string> mutated = MutatorTestBase.MutatedTexts(
            Mutator, "bool a, bool b, int x, int y", "int M({0}) => a || b ? x : y;");

        Assert.Equal(["((a || b) || true) ? x : y", "((a || b) && false) ? x : y"], mutated);
    }

    /// <summary>The RB-001 guard: every replacement must still be a conditional expression.</summary>
    [Fact]
    public void Every_replacement_carries_the_kind_it_prints()
    {
        Assert.All(
            MutatorTestBase.MutateAll(Mutator, "a ? x : y"),
            candidate => Assert.True(candidate.Replacement.IsKind(SyntaxKind.ConditionalExpression)));
    }

    /// <summary>Forcing a choice between two identical branches changes nothing.</summary>
    [Fact]
    public void A_ternary_with_identical_branches_is_not_mutated()
    {
        Assert.Empty(MutatorTestBase.MutateAll(Mutator, "a ? x : x"));
    }

    /// <summary>
    /// A constant condition was settled when the code was compiled, so there is no decision to force.
    /// </summary>
    [Fact]
    public void A_constant_condition_is_not_forced()
    {
        Assert.Empty(MutatorTestBase.MutateAll(
            Mutator, "int x, int y", "int M({0}) { const bool On = true; return On ? x : y; }"));
    }

    /// <summary>
    /// A condition of a type with its own <c>operator true</c> is not a <c>bool</c>, and
    /// <c>c || true</c> is not this family's to write for it.
    /// </summary>
    [Fact]
    public void A_condition_that_is_not_a_bool_is_not_forced()
    {
        Assert.Empty(MutatorTestBase.MutateAll(
            Mutator,
            "Flag f, int x, int y",
            "int M({0}) => f ? x : y; } public struct Flag { public static bool operator true(Flag f) => true; " +
            "public static bool operator false(Flag f) => false;"));
    }

    /// <summary>
    /// A conditional need not have a natural type - `a ? 1 : null` only gets one from its target -
    /// so the binding check must not treat a null type as a failure.
    /// </summary>
    [Fact]
    public void A_target_typed_conditional_is_mutated()
    {
        IReadOnlyList<string> mutated = MutatorTestBase.MutatedTexts(
            Mutator, "bool a, int x", "int? M({0}) => a ? x : null;");

        Assert.Equal(["(a || true) ? x : null", "(a && false) ? x : null"], mutated);
    }

    [Fact]
    public void Nested_conditionals_each_yield_their_own_mutants()
    {
        IReadOnlyList<string> mutated = MutatorTestBase.MutatedTexts(
            Mutator, "bool a, bool b, int x, int y", "int M({0}) => a ? x : b ? y : 0;");

        Assert.Equal(
            ["(a || true) ? x : b ? y : 0", "(a && false) ? x : b ? y : 0", "(b || true) ? y : 0", "(b && false) ? y : 0"],
            mutated);
    }

    [Theory]
    [InlineData("x >= y")]
    [InlineData("a && b")]
    public void Expressions_outside_this_family_are_left_alone(string expression)
    {
        Assert.Empty(MutatorTestBase.MutateAll(Mutator, expression));
    }

    [Fact]
    public void The_mutations_are_attributed_to_this_family()
    {
        Assert.All(
            MutatorTestBase.MutateAll(Mutator, "a ? x : y"),
            candidate => Assert.Equal("Conditional", candidate.Mutator.ToString()));
    }
}
