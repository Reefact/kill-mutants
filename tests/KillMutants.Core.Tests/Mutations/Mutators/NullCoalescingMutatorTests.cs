using Microsoft.CodeAnalysis;
using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KillMutants.Core.Tests.Mutations.Mutators;

public class NullCoalescingMutatorTests
{
    private static readonly NullCoalescingMutator Mutator = new();

    [Fact]
    public void The_fallback_is_dropped_and_the_operands_are_swapped()
    {
        IReadOnlyList<string> mutated = MutatorTestBase.MutatedTexts(
            Mutator, "string s, string t", "object M({0}) => s ?? t;");

        Assert.Equal(["s", "t ?? s"], mutated);
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

        Assert.Equal(["s", "o ?? s"], mutated);
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

    /// <summary>
    /// Regression test (#17). The shape the swap exists for: one setting read from two places,
    /// where only the swap asks which of them wins.
    /// </summary>
    [Fact]
    public void Two_settings_read_from_two_places_are_swapped()
    {
        IReadOnlyList<string> mutated = MutatorTestBase.MutatedTexts(
            Mutator,
            "System.Tuple<string?> commandLine, System.Tuple<string?> saved",
            "string? M({0}) => commandLine.Item1 ?? saved.Item1;");

        Assert.Equal(["commandLine.Item1", "saved.Item1 ?? commandLine.Item1"], mutated);
    }

    [Fact]
    public void Two_nullable_values_are_swapped()
    {
        IReadOnlyList<string> mutated = MutatorTestBase.MutatedTexts(
            Mutator, "int? a, int? b", "int? M({0}) => a ?? b;");

        Assert.Equal(["a", "b ?? a"], mutated);
    }

    /// <summary>
    /// The left operand of <c>??</c> has to be able to be null, so a fallback that cannot be has
    /// nowhere to go: <c>fallback ?? count</c> does not compile, and is not proposed (RB-011).
    /// </summary>
    [Fact]
    public void A_fallback_that_cannot_be_null_is_not_swapped()
    {
        IReadOnlyList<string> mutated = MutatorTestBase.MutatedTexts(
            Mutator, "int? count, int fallback", "int? M({0}) => count ?? fallback;");

        Assert.Equal(["count"], mutated);
    }

    /// <summary>
    /// <c>t ?? next()</c> calls <c>next</c> only when <c>t</c> is null, so the swap would drop the
    /// call - the side effect the first mutation refuses to lose, lost by the second.
    /// </summary>
    [Fact]
    public void A_left_operand_that_does_more_than_read_is_not_swapped()
    {
        IReadOnlyList<string> mutated = MutatorTestBase.MutatedTexts(
            Mutator, "System.Func<string?> next, string t", "string M({0}) => next() ?? t;");

        Assert.Equal(["next()"], mutated);
    }

    /// <summary><c>null ?? s</c> is <c>s</c>: the first mutation, judged a second time.</summary>
    [Theory]
    [InlineData("s ?? null")]
    [InlineData("s ?? default")]
    [InlineData("s ?? default(string)")]
    public void A_fallback_of_nothing_is_not_swapped(string expression)
    {
        IReadOnlyList<string> mutated = MutatorTestBase.MutatedTexts(
            Mutator, "string? s", $"string? M({{0}}) => {expression};");

        Assert.Equal(["s"], mutated);
    }

    /// <summary>
    /// The same expression on both sides swaps into the original, and can never be killed.
    /// </summary>
    [Fact]
    public void The_same_expression_on_both_sides_is_not_swapped()
    {
        IReadOnlyList<string> mutated = MutatorTestBase.MutatedTexts(
            Mutator, "string? s", "string? M({0}) => s ?? s;");

        Assert.Equal(["s"], mutated);
    }

    /// <summary>
    /// <c>a ?? b ?? c</c> is <c>a ?? (b ?? c)</c>, and swapping the outer one would print as
    /// <c>b ?? c ?? a</c>, which reads back as a different tree from the one compiled. The inner
    /// one still swaps.
    /// </summary>
    [Fact]
    public void A_chained_fallback_is_swapped_only_where_it_reads_back_as_written()
    {
        IReadOnlyList<string> mutated = MutatorTestBase.MutatedTexts(
            Mutator, "string? a, string? b, string c", "string M({0}) => a ?? b ?? c;");

        Assert.Equal(["a", "b", "c ?? b"], mutated);
    }

    /// <summary>
    /// Swapping operands rather than an operator, the RB-001 check is that the replacement is a
    /// coalesce node of its own, with the operands in their new places.
    /// </summary>
    [Fact]
    public void The_swap_is_a_new_coalesce_node()
    {
        MutationCandidate candidate = MutatorTestBase.MutateAll(
            Mutator, "string s, string t", "object M({0}) => s ?? t;")[1];

        BinaryExpressionSyntax swapped =
            Assert.IsType<BinaryExpressionSyntax>(candidate.Replacement);

        Assert.True(swapped.IsKind(SyntaxKind.CoalesceExpression));
        Assert.Equal("t", swapped.Left.ToString());
        Assert.Equal("s", swapped.Right.ToString());
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
