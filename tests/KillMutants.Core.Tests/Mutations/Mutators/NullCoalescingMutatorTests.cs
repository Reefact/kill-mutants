using Microsoft.CodeAnalysis;
using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;
using Microsoft.CodeAnalysis.CSharp;

namespace KillMutants.Core.Tests.Mutations.Mutators;

public class NullCoalescingMutatorTests
{
    private static readonly NullCoalescingMutator Mutator = new();

    [Fact]
    public void The_fallback_is_dropped()
    {
        IReadOnlyList<string> mutated = MutatorTestBase.MutatedTexts(
            Mutator, "string s, string t", "object M({0}) => s ?? t;");

        Assert.Equal(["s"], mutated);
    }

    /// <summary>
    /// The reason this family needs the semantic model. `??` is often there to remove nullability,
    /// and there the left operand does not fit on its own: `int total = count ?? 0` mutated to
    /// `int total = count` is a compile error, which costs a run and teaches nothing.
    /// </summary>
    [Fact]
    public void A_fallback_that_removes_nullability_is_not_dropped()
    {
        Assert.Empty(MutatorTestBase.MutateAll(
            Mutator, "int? count", "int M({0}) => count ?? 0;"));
    }

    /// <summary>The conversion check accepts widening, so a reference-typed fallback stays mutable.</summary>
    [Fact]
    public void A_left_operand_that_widens_to_the_expected_type_is_dropped()
    {
        IReadOnlyList<string> mutated = MutatorTestBase.MutatedTexts(
            Mutator, "string s, object o", "object M({0}) => s ?? o;");

        Assert.Equal(["s"], mutated);
    }

    /// <summary>
    /// The replacement is the left operand itself, so it carries that operand's kind rather than a
    /// coalesce expression - the RB-001 check for a family that deletes rather than swaps.
    /// </summary>
    [Fact]
    public void The_replacement_is_the_left_operand()
    {
        MutationCandidate candidate = MutatorTestBase.MutateAll(
            Mutator, "string s, string t", "object M({0}) => s ?? t;")[0];

        Assert.True(candidate.Replacement.IsKind(SyntaxKind.IdentifierName));
    }

    /// <summary>A null-conditional access is a different node, and a different mutation.</summary>
    [Fact]
    public void A_null_conditional_access_is_left_alone()
    {
        Assert.Empty(MutatorTestBase.MutateAll(
            Mutator, "string s", "object? M({0}) => s?.Length;"));
    }

    [Theory]
    [InlineData("x + y")]
    [InlineData("a && b")]
    public void Operators_outside_this_family_are_left_alone(string expression)
    {
        Assert.Empty(MutatorTestBase.MutateAll(Mutator, expression));
    }

    [Fact]
    public void The_mutations_are_attributed_to_this_family()
    {
        Assert.All(
            MutatorTestBase.MutateAll(Mutator, "string s, string t", "object M({0}) => s ?? t;"),
            candidate => Assert.Equal("NullCoalescing", candidate.Mutator.ToString()));
    }
}
