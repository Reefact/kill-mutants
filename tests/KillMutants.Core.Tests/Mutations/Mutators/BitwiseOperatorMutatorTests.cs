using Microsoft.CodeAnalysis;
using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;
using Microsoft.CodeAnalysis.CSharp;

namespace KillMutants.Core.Tests.Mutations.Mutators;

public class BitwiseOperatorMutatorTests
{
    private static readonly BitwiseOperatorMutator Mutator = new();

    [Theory]
    [InlineData("x & y", "x | y")]
    [InlineData("x | y", "x & y")]
    [InlineData("x ^ y", "x & y")]
    [InlineData("x << y", "x >> y")]
    [InlineData("x >> y", "x << y")]
    public void A_bitwise_operator_is_replaced(string expression, string expected)
    {
        Assert.Equal([expected], MutatorTestBase.MutatedTexts(Mutator, expression));
    }

    /// <summary>
    /// `&amp;` and `|` on booleans are the non-short-circuiting forms of `&amp;&amp;` and `||`, and
    /// the same rewrite applies: no separate rule, and no need to distinguish the two at this level.
    /// </summary>
    [Theory]
    [InlineData("a & b", "a | b")]
    [InlineData("a | b", "a & b")]
    public void The_non_short_circuiting_boolean_forms_are_mutated_too(string expression, string expected)
    {
        Assert.Equal([expected], MutatorTestBase.MutatedTexts(Mutator, expression));
    }

    /// <summary>The RB-001 guard, applied to this family.</summary>
    [Fact]
    public void Every_replacement_carries_the_kind_it_prints()
    {
        (string Expression, SyntaxKind Kind)[] cases =
        [
            ("x & y", SyntaxKind.BitwiseOrExpression),
            ("x | y", SyntaxKind.BitwiseAndExpression),
            ("x ^ y", SyntaxKind.BitwiseAndExpression),
            ("x << y", SyntaxKind.RightShiftExpression),
            ("x >> y", SyntaxKind.LeftShiftExpression),
        ];

        foreach ((string expression, SyntaxKind kind) in cases)
        {
            MutationCandidate first = MutatorTestBase.MutateAll(Mutator, expression)[0];

            Assert.True(
                first.Replacement.IsKind(kind),
                $"'{expression}' produced kind {first.Replacement.Kind()}, expected {kind}.");
        }
    }

    /// <summary>
    /// The binding check inherited from the base class covers this family too: a type declaring only
    /// `operator &amp;` has no `|` to swap in.
    /// </summary>
    [Fact]
    public void A_type_that_declares_only_one_operator_of_the_pair_is_not_mutated()
    {
        Assert.Empty(MutatorTestBase.MutateAll(Mutator, "Money left, Money right", "object M({0}) => left & right;"));
    }

    [Theory]
    [InlineData("a && b")]
    [InlineData("x + y")]
    public void Operators_outside_this_family_are_left_to_their_own_mutator(string expression)
    {
        Assert.Empty(MutatorTestBase.MutateAll(Mutator, expression));
    }

    [Fact]
    public void The_mutations_are_attributed_to_this_family()
    {
        Assert.All(
            MutatorTestBase.MutateAll(Mutator, "x & y"),
            candidate => Assert.Equal("Bitwise", candidate.Mutator.ToString()));
    }
}
