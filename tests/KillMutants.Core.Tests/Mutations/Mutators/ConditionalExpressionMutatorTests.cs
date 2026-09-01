using Microsoft.CodeAnalysis;
using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;
using Microsoft.CodeAnalysis.CSharp;

namespace KillMutants.Core.Tests.Mutations.Mutators;

public class ConditionalExpressionMutatorTests
{
    private static readonly ConditionalExpressionMutator Mutator = new();

    /// <summary>
    /// The condition is left untouched - that is the other families' job - and here it is a bare
    /// identifier with nothing to mutate, which is exactly why this family earns its place.
    /// </summary>
    [Fact]
    public void The_branches_are_swapped()
    {
        Assert.Equal(["a ? y : x"], MutatorTestBase.MutatedTexts(Mutator, "a ? x : y"));
    }

    /// <summary>The RB-001 guard: the replacement must still be a conditional expression.</summary>
    [Fact]
    public void Every_replacement_carries_the_kind_it_prints()
    {
        MutationCandidate candidate = MutatorTestBase.MutateAll(Mutator, "a ? x : y")[0];

        Assert.True(candidate.Replacement.IsKind(SyntaxKind.ConditionalExpression));
    }

    /// <summary>Swapping two identical branches changes nothing, so no mutant is proposed.</summary>
    [Fact]
    public void Identical_branches_are_not_swapped()
    {
        Assert.Empty(MutatorTestBase.MutateAll(Mutator, "a ? x : x"));
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

        Assert.Equal(["a ? null : x"], mutated);
    }

    [Fact]
    public void Nested_conditionals_each_yield_their_own_mutant()
    {
        IReadOnlyList<string> mutated = MutatorTestBase.MutatedTexts(
            Mutator, "bool a, bool b, int x, int y", "int M({0}) => a ? x : b ? y : 0;");

        Assert.Equal(["a ? b ? y : 0 : x", "b ? 0 : y"], mutated);
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
