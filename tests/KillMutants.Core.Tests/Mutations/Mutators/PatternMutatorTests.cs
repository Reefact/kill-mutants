using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KillMutants.Core.Tests.Mutations.Mutators;

public class PatternMutatorTests
{
    private static readonly PatternMutator Mutator = new();

    /// <summary>
    /// Regression test (#19, DEC0018). An <c>is</c> is negated as a pattern, wherever it stands -
    /// including where no decision is taken, which <c>NegatedDecision</c> never reached.
    /// </summary>
    [Theory]
    [InlineData("bool M({0}) => o is null;", "o is not null")]
    [InlineData("bool M({0}) => o is not null;", "o is null")]
    [InlineData("bool M({0}) => o is string;", "o is not string")]
    [InlineData("bool M({0}) { bool b = o is string; return b; }", "o is not string")]
    public void An_is_expression_is_negated_as_a_pattern(string member, string expected)
    {
        Assert.Contains(expected, MutatorTestBase.MutatedTexts(Mutator, "object? o", member));
    }

    /// <summary>
    /// Regression test (DEC0018, measured on Reefact/slugger). <c>d is DayOfWeek.Monday</c> reads as
    /// a type test, but names a constant: negated as a type pattern it fails with CS0426.
    /// </summary>
    [Fact]
    public void An_enum_member_tested_like_a_type_is_negated_as_a_constant()
    {
        MutationCandidate candidate = MutatorTestBase.MutateAll(
            Mutator, "System.DayOfWeek d", "bool M({0}) => d is System.DayOfWeek.Monday;")[0];

        Assert.Equal("d is not System.DayOfWeek.Monday", candidate.Replacement.ToString());
        Assert.IsType<ConstantPatternSyntax>(((UnaryPatternSyntax)((IsPatternExpressionSyntax)candidate.Replacement).Pattern).Pattern);
    }

    /// <summary>
    /// <c>not</c> binds tighter than <c>or</c>: negating <c>s is "a" or "b"</c> without parentheses
    /// would read <c>(not "a") or "b"</c>.
    /// </summary>
    [Fact]
    public void An_or_pattern_is_negated_whole()
    {
        Assert.Contains(
            "s is not (\"a\" or \"b\")",
            MutatorTestBase.MutatedTexts(Mutator, "string s", "bool M({0}) => s is \"a\" or \"b\";"));
    }

    /// <summary>
    /// A relational pattern inside a larger one gets both of <c>Comparison</c>'s mutants: the
    /// boundary shift and the negation.
    /// </summary>
    [Fact]
    public void A_nested_relational_pattern_is_shifted_and_negated()
    {
        IReadOnlyList<string> mutated = MutatorTestBase.MutatedTexts(
            Mutator, "string s", "bool M({0}) => s is { Length: > 1 };");

        Assert.Contains(">= 1", mutated);
        Assert.Contains("<= 1", mutated);
    }

    /// <summary>
    /// As the whole pattern, a relational pattern only has its boundary shifted: its negation would
    /// ask what negating the <c>is</c> already asks.
    /// </summary>
    [Fact]
    public void A_relational_pattern_that_is_the_whole_test_is_only_shifted()
    {
        IReadOnlyList<string> mutated = MutatorTestBase.MutatedTexts(
            Mutator, "int x", "bool M({0}) => x is > 1;");

        Assert.Equal(["x is not > 1", ">= 1"], mutated);
    }

    /// <summary>Each arm of an <c>or</c> is dropped in turn, asking whether a test needs it.</summary>
    [Fact]
    public void Each_arm_of_an_or_is_dropped_in_turn()
    {
        IReadOnlyList<string> mutated = MutatorTestBase.MutatedTexts(
            Mutator, "string s", "bool M({0}) => s is \".\" or \"..\";");

        Assert.Contains("\".\"", mutated);
        Assert.Contains("\"..\"", mutated);
    }

    /// <summary>
    /// The arm beside <c>not null</c> already rejects <c>null</c>, so dropping it would change
    /// nothing; the other arm is still dropped.
    /// </summary>
    [Fact]
    public void An_and_keeps_its_null_check()
    {
        IReadOnlyList<string> mutated = MutatorTestBase.MutatedTexts(
            Mutator, "string? s", "bool M({0}) => s is not null and { Length: > 0 };");

        Assert.DoesNotContain("{ Length: > 0 }", mutated);
        Assert.Contains("not null", mutated);
    }

    /// <summary>
    /// Regression test (review of #38). The arm beside <c>not null</c> matches <c>null</c> here, so
    /// dropping the null check changes what matches - and asks whether a test passes <c>null</c>.
    /// </summary>
    [Fact]
    public void An_and_drops_its_null_check_when_the_other_arm_matches_null()
    {
        IReadOnlyList<string> mutated = MutatorTestBase.MutatedTexts(
            Mutator, "string? s", "bool M({0}) => s is not null and (null or \"a\");");

        Assert.Contains("(null or \"a\")", mutated);

        // Dropping "a" would leave `not null and null`, a pattern the compiler refuses as one that
        // never matches: the mutant is asked of the compiler first, and not proposed (RB-011).
        Assert.DoesNotContain("null", mutated);
    }

    /// <summary>
    /// Regression test (review of #38). A named constant holding <c>null</c> matches it as the
    /// literal does, so <c>(N or "a")</c> lets <c>null</c> through and the null check is dropped.
    /// </summary>
    [Fact]
    public void An_and_drops_its_null_check_when_the_other_arm_matches_a_named_null()
    {
        IReadOnlyList<string> mutated = MutatorTestBase.MutatedTexts(
            Mutator,
            "string? s",
            "const string? N = null; bool M({0}) => s is not null and (N or \"a\");");

        Assert.Contains("(N or \"a\")", mutated);
    }

    /// <summary>
    /// A named constant holding anything but <c>null</c> rejects it, so the null check stays.
    /// </summary>
    [Fact]
    public void An_and_keeps_its_null_check_beside_a_named_constant_that_is_not_null()
    {
        IReadOnlyList<string> mutated = MutatorTestBase.MutatedTexts(
            Mutator,
            "string? s",
            "const string A = \"a\"; bool M({0}) => s is not null and (A or \"b\");");

        Assert.DoesNotContain("(A or \"b\")", mutated);
    }

    /// <summary>
    /// Regression test (review of #38). The right arm of <c>o is string and { Length: &gt; 0 }</c>
    /// is matched against a <c>string</c>: alone, it would read a <c>Length</c> an <c>object</c>
    /// does not have. The left arm can still stand alone.
    /// </summary>
    [Fact]
    public void An_and_keeps_the_arm_that_narrows_the_type_the_other_reads()
    {
        IReadOnlyList<string> mutated = MutatorTestBase.MutatedTexts(
            Mutator, "object o", "bool M({0}) => o is string and { Length: > 0 };");

        Assert.DoesNotContain("{ Length: > 0 }", mutated);
        Assert.Contains("string", mutated);
    }

    /// <summary>
    /// Regression test (review of #38). An expression tree accepts the type-test operator and no
    /// pattern (CS8122), so the type test is negated around its operator there.
    /// </summary>
    [Fact]
    public void A_type_test_in_an_expression_tree_is_negated_around_its_operator()
    {
        IReadOnlyList<string> mutated = MutatorTestBase.MutatedTexts(
            Mutator,
            "int a",
            "object M({0}) { System.Linq.Expressions.Expression<System.Func<object, bool>> f = x => x is string; return f; }");

        Assert.Equal(["!(x is string)"], mutated);
    }

    /// <summary>
    /// A pattern in a switch is left alone: changing one arm can make a later one unreachable, which
    /// the compiler refuses for the whole switch.
    /// </summary>
    [Theory]
    [InlineData("int M({0}) => x switch { > 10 => 1, > 5 or < 0 => 2, _ => 0 };")]
    [InlineData("int M({0}) { switch (x) { case > 10: return 1; default: return 0; } }")]
    public void A_pattern_in_a_switch_is_left_alone(string member)
    {
        Assert.Empty(MutatorTestBase.MutateAll(Mutator, "int x", member));
    }

    /// <summary>The RB-001 guard: every replacement carries the kind of the node it replaces.</summary>
    [Fact]
    public void Every_replacement_carries_the_kind_it_prints()
    {
        Assert.All(
            MutatorTestBase.MutateAll(Mutator, "int x", "bool M({0}) => x is (> 1 and < 9) or 0;"),
            candidate => Assert.True(
                candidate.Original is PatternSyntax
                    ? candidate.Replacement is PatternSyntax
                    : candidate.Replacement is IsPatternExpressionSyntax));
    }

    [Fact]
    public void The_mutations_are_attributed_to_this_family()
    {
        Assert.All(
            MutatorTestBase.MutateAll(Mutator, "object? o", "bool M({0}) => o is null;"),
            candidate => Assert.Equal("Pattern", candidate.Mutator.ToString()));
    }

    /// <summary>
    /// Regression test (#50). <c>PlatformNotSupportedException</c> derives from
    /// <c>NotSupportedException</c>, so dropping it from the <c>or</c> changes nothing that matches;
    /// dropping the base type does narrow the test, and stays.
    /// </summary>
    [Fact]
    public void An_or_keeps_a_type_its_other_arm_already_covers()
    {
        IReadOnlyList<string> mutated = MutatorTestBase.MutatedTexts(
            Mutator, "System.Exception e", "bool M({0}) => e is System.NotSupportedException or System.PlatformNotSupportedException;");

        Assert.DoesNotContain("System.NotSupportedException", mutated);
        Assert.Contains("System.PlatformNotSupportedException", mutated);
    }

    /// <summary>Two unrelated types each say something the other does not, so both arms are dropped in turn.</summary>
    [Fact]
    public void An_or_of_unrelated_types_drops_each_arm()
    {
        IReadOnlyList<string> mutated = MutatorTestBase.MutatedTexts(
            Mutator, "System.Exception e", "bool M({0}) => e is System.ArgumentException or System.InvalidOperationException;");

        Assert.Contains("System.ArgumentException", mutated);
        Assert.Contains("System.InvalidOperationException", mutated);
    }
}
