using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;

namespace KillMutants.Core.Tests.Mutations.Mutators;

public class ArithmeticOperatorMutatorTests
{
    private static readonly ArithmeticOperatorMutator Mutator = new();

    [Theory]
    [InlineData("x + y", "x - y")]
    [InlineData("x - y", "x + y")]
    [InlineData("x * y", "x / y")]
    [InlineData("x / y", "x * y")]
    [InlineData("x % y", "x * y")]
    public void An_arithmetic_operator_is_replaced(string expression, string expected)
    {
        Assert.Equal([expected], MutatorTestBase.MutatedTexts(Mutator, expression));
    }

    /// <summary>
    /// The reason this family needs the semantic model. `"a" - "b"` does not exist, so mutating a
    /// concatenation could only ever produce a mutant that fails to compile: pure cost, no signal.
    /// </summary>
    [Theory]
    [InlineData("string s, string t", "object M(string s, string t) => s + t;")]
    [InlineData("string s, int n", "object M(string s, int n) => s + n;")]
    [InlineData("int n, string s", "object M(int n, string s) => n + s;")]
    public void String_concatenation_is_not_mutated(string parameters, string member)
    {
        Assert.Empty(MutatorTestBase.MutateAll(Mutator, parameters, member.Replace(parameters, "{0}", StringComparison.Ordinal)));
    }

    /// <summary>
    /// The guard is general rather than a list of special cases: a type declaring only `operator +`
    /// is rejected by exactly the same rule that rejects string concatenation.
    /// </summary>
    [Fact]
    public void A_type_that_declares_only_one_operator_of_the_pair_is_not_mutated()
    {
        IReadOnlyList<MutationCandidate> candidates = MutatorTestBase.MutateAll(
            Mutator,
            "Money left, Money right",
            "object M({0}) => left + right;");

        Assert.Empty(candidates);
    }

    /// <summary>Delegates define both `+` and `-`, so the same rule lets them through.</summary>
    [Fact]
    public void A_type_that_declares_both_operators_is_mutated()
    {
        IReadOnlyList<string> mutated = MutatorTestBase.MutatedTexts(
            Mutator, "System.Action? f, System.Action? g", "object? M({0}) => f + g;");

        Assert.Equal(["f - g"], mutated);
    }

    [Theory]
    [InlineData("x >= y")]
    [InlineData("a && b")]
    public void Operators_outside_this_family_are_left_alone(string expression)
    {
        Assert.Empty(MutatorTestBase.MutateAll(Mutator, expression));
    }
}
