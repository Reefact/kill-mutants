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
    /// `int total = count` is a compile error, which costs a run and teaches nothing. The forced
    /// fallback has no such problem - `0` is an `int` wherever the original stood - and the swap
    /// cannot bind, so it is the one mutant (#77).
    /// </summary>
    [Fact]
    public void A_fallback_that_removes_nullability_is_not_dropped()
    {
        IReadOnlyList<string> mutated = MutatorTestBase.MutatedTexts(
            Mutator, "int? count", "int M({0}) => count ?? 0;");

        Assert.Equal(["(count is null ? 0 : 0)"], mutated);
    }

    /// <summary>
    /// The line that found #77, on `Reefact/enum-member-name-binding` at `bc6eda9`: the fallback
    /// dropped is killed, the swap is refused for the call, and only the fallback forced asks
    /// whether a nullable enum ever reaches the line - none did, and no test noticed.
    /// </summary>
    [Fact]
    public void A_left_that_runs_code_is_forced_to_its_fallback_and_still_evaluated()
    {
        IReadOnlyList<string> mutated = MutatorTestBase.MutatedTexts(
            Mutator, "System.Type t", "System.Type M({0}) => System.Nullable.GetUnderlyingType(t) ?? t;");

        Assert.Equal(
            ["System.Nullable.GetUnderlyingType(t)", "(System.Nullable.GetUnderlyingType(t) is null ? t : t)"],
            mutated);
    }

    /// <summary>
    /// An expression tree accepts no pattern (CS8122), and the forced form tests the left with
    /// one, so inside a tree the fallback is dropped and nothing is forced (review of #81).
    /// </summary>
    [Fact]
    public void A_coalesce_inside_an_expression_tree_is_not_forced()
    {
        IReadOnlyList<string> mutated = MutatorTestBase.MutatedTexts(
            Mutator,
            "int unused",
            "System.Linq.Expressions.Expression<System.Func<string?, string>> M({0}) => s => s.ToString() ?? \"x\";");

        Assert.Equal(["s.ToString()"], mutated);
    }

    /// <summary>A <c>throw</c> has no type to give both branches of the forced form.</summary>
    [Fact]
    public void A_throw_fallback_is_not_forced()
    {
        IReadOnlyList<string> mutated = MutatorTestBase.MutatedTexts(
            Mutator, "string? s", "string M({0}) => s ?? throw new System.InvalidOperationException();");

        Assert.Equal(["s"], mutated);
    }

    /// <summary>
    /// The forced form is a conditional of its own, parenthesised, with the fallback in both
    /// branches and the left operand tested for null - the RB-001 check for this mutation.
    /// </summary>
    [Fact]
    public void The_forced_fallback_is_a_parenthesised_conditional_on_the_left_being_null()
    {
        MutationCandidate candidate = MutatorTestBase.MutateAll(
            Mutator, "System.Func<string?> next, string t", "string M({0}) => next() ?? t;")[1];

        ParenthesizedExpressionSyntax parenthesised =
            Assert.IsType<ParenthesizedExpressionSyntax>(candidate.Replacement);
        ConditionalExpressionSyntax forced =
            Assert.IsType<ConditionalExpressionSyntax>(parenthesised.Expression);

        Assert.Equal("next() is null", forced.Condition.ToString());
        Assert.Equal("t", forced.WhenTrue.ToString());
        Assert.Equal("t", forced.WhenFalse.ToString());
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
            "Settings commandLine, Settings saved",
            "record Settings(string? Separator); " +
            "public string? M({0}) => commandLine.Separator ?? saved.Separator;");

        Assert.Equal(
            ["commandLine.Separator", "saved.Separator ?? commandLine.Separator"], mutated);
    }

    /// <summary>
    /// Regression test, found in review. A getter is code: this one counts its reads, and
    /// <c>fallback ?? reader.Next</c> would skip it whenever the fallback is set, so the swap could
    /// be killed by the read it dropped rather than by the precedence it asks about. The forced
    /// fallback keeps the getter running, so it is made instead.
    /// </summary>
    [Fact]
    public void A_property_whose_getter_runs_code_is_not_swapped()
    {
        IReadOnlyList<string> mutated = MutatorTestBase.MutatedTexts(
            Mutator,
            "Reader reader, string fallback",
            "class Reader { int _reads; public string? Next { get { _reads++; return null; } } } " +
            "public string M({0}) => reader.Next ?? fallback;");

        Assert.Equal(["reader.Next", "(reader.Next is null ? fallback : fallback)"], mutated);
    }

    /// <summary>
    /// What the compiler stores itself reads without running anything: a property it implements as
    /// a field, and a field.
    /// </summary>
    [Theory]
    [InlineData("class Holder { public string? Value { get; init; } }")]
    [InlineData("class Holder { public static int Unused; public string? Value; }")]
    public void A_value_the_compiler_stores_is_swapped(string holder)
    {
        IReadOnlyList<string> mutated = MutatorTestBase.MutatedTexts(
            Mutator,
            "Holder a, Holder b",
            holder + " public string? M({0}) => a.Value ?? b.Value;");

        Assert.Equal(["a.Value", "b.Value ?? a.Value"], mutated);
    }

    /// <summary>
    /// A property whose getter cannot be read from here, or can be replaced, may run anything: one
    /// with an expression body, a virtual one whose override is elsewhere, and one from another
    /// assembly.
    /// </summary>
    [Theory]
    [InlineData("class Holder { public string? Value => null; }", "Holder")]
    [InlineData("class Holder { public virtual string? Value { get; init; } }", "Holder")]
    [InlineData("", "System.Tuple<string?>")]
    public void A_property_that_may_run_code_is_not_swapped(string holder, string type)
    {
        string read = type == "Holder" ? "Value" : "Item1";

        IReadOnlyList<string> mutated = MutatorTestBase.MutatedTexts(
            Mutator,
            $"{type} a, {type} b",
            $"{holder} public string? M({{0}}) => a.{read} ?? b.{read};");

        Assert.Equal([$"a.{read}", $"(a.{read} is null ? b.{read} : b.{read})"], mutated);
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
    /// nowhere to go: <c>fallback ?? count</c> does not compile, and is not proposed (RB-011). The
    /// fallback forced is an <c>int</c> where an <c>int?</c> stood, and is.
    /// </summary>
    [Fact]
    public void A_fallback_that_cannot_be_null_is_not_swapped()
    {
        IReadOnlyList<string> mutated = MutatorTestBase.MutatedTexts(
            Mutator, "int? count, int fallback", "int? M({0}) => count ?? fallback;");

        Assert.Equal(["count", "(count is null ? fallback : fallback)"], mutated);
    }

    /// <summary>
    /// <c>t ?? next()</c> calls <c>next</c> only when <c>t</c> is null, so the swap would drop the
    /// call - the side effect the first mutation refuses to lose, lost by the second. Forcing the
    /// fallback keeps the call, so that is the mutant made instead.
    /// </summary>
    [Fact]
    public void A_left_operand_that_does_more_than_read_is_not_swapped()
    {
        IReadOnlyList<string> mutated = MutatorTestBase.MutatedTexts(
            Mutator, "System.Func<string?> next, string t", "string M({0}) => next() ?? t;");

        Assert.Equal(["next()", "(next() is null ? t : t)"], mutated);
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
    /// Regression test, found in review. <c>null ?? s</c> and <c>s ?? null</c> both answer
    /// <c>s</c>, null or not, so the swap would be a mutant no test could kill. Dropping the
    /// fallback still applies: <c>null</c> alone is a different answer.
    /// </summary>
    [Theory]
    [InlineData("null ?? s", "null")]
    [InlineData("default(string) ?? s", "default(string)")]
    public void Nothing_on_the_left_is_not_swapped(string expression, string dropped)
    {
        IReadOnlyList<string> mutated = MutatorTestBase.MutatedTexts(
            Mutator, "string? s", $"string? M({{0}}) => {expression};");

        Assert.Equal([dropped], mutated);
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
    /// <c>b ?? c ?? a</c>, which reads back as a different tree from the one compiled. The outer one
    /// is forced instead, its parentheses keeping the chain inside; the inner one still swaps.
    /// </summary>
    [Fact]
    public void A_chained_fallback_is_swapped_only_where_it_reads_back_as_written()
    {
        IReadOnlyList<string> mutated = MutatorTestBase.MutatedTexts(
            Mutator, "string? a, string? b, string c", "string M({0}) => a ?? b ?? c;");

        Assert.Equal(["a", "(a is null ? b ?? c : b ?? c)", "b", "c ?? b"], mutated);
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
