using Microsoft.CodeAnalysis;
using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;
using Microsoft.CodeAnalysis.CSharp;

namespace KillMutants.Core.Tests.Mutations.Mutators;

public class IncrementMutatorTests
{
    private static readonly IncrementMutator Mutator = new();

    [Theory]
    [InlineData("x++", "x--")]
    [InlineData("x--", "x++")]
    [InlineData("++x", "--x")]
    [InlineData("--x", "++x")]
    public void An_increment_is_replaced_by_its_opposite(string statement, string expected)
    {
        Assert.Equal([expected], Mutate(statement));
    }

    /// <summary>The RB-001 guard, applied to this family, in both positions.</summary>
    [Fact]
    public void Every_replacement_carries_the_kind_it_prints()
    {
        (string Statement, SyntaxKind Kind)[] cases =
        [
            ("x++", SyntaxKind.PostDecrementExpression),
            ("x--", SyntaxKind.PostIncrementExpression),
            ("++x", SyntaxKind.PreDecrementExpression),
            ("--x", SyntaxKind.PreIncrementExpression),
        ];

        foreach ((string statement, SyntaxKind kind) in cases)
        {
            MutationCandidate first = Candidates(statement)[0];

            Assert.True(
                first.Replacement.IsKind(kind),
                $"'{statement}' produced kind {first.Replacement.Kind()}, expected {kind}.");
        }
    }

    /// <summary>
    /// The position is part of the meaning: `x++` and `++x` differ in what the expression evaluates
    /// to, so a prefix must stay a prefix.
    /// </summary>
    [Fact]
    public void The_position_of_the_operator_is_preserved()
    {
        Assert.StartsWith("--", Mutate("++x")[0], StringComparison.Ordinal);
        Assert.EndsWith("--", Mutate("x++")[0], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("x = -x")]
    [InlineData("x = +x")]
    public void Unary_signs_are_left_alone(string statement)
    {
        Assert.Empty(Candidates(statement));
    }

    /// <summary>
    /// The reason this family needs the semantic model, and the gap it was shipped with: `++` and
    /// `--` are a pair only for the built-in numeric types. Measured against the .NET 10 SDK, a type
    /// declaring one and not the other yields
    /// `CS0023: Operator '--' cannot be applied to operand of type 'Counter'`.
    /// </summary>
    [Fact]
    public void A_type_that_declares_only_one_operator_of_the_pair_is_not_mutated()
    {
        // The operator bodies avoid ++ and -- so that the only candidate site is `c++` itself.
        Assert.Empty(MutateSource(
            "public struct Counter { public int Value; " +
            "public static Counter operator ++(Counter c) { c.Value = c.Value + 1; return c; } } " +
            "public class C { public void M(Counter c) { c++; } }"));
    }

    /// <summary>
    /// C# 14 added user-defined <em>instance</em> increment operators, which are a second way to
    /// declare one half of the pair. Asking the compiler covers it without this family knowing it
    /// exists - and would cover whatever C# adds after it.
    /// </summary>
    [Fact]
    public void The_same_holds_for_a_user_defined_instance_operator()
    {
        Assert.Empty(MutateSource(
            "public class Counter { public int Value; public void operator ++() { Value = Value + 1; } } " +
            "public class C { public void M(Counter c) { c++; } }"));
    }

    /// <summary>The guard must stay narrow: a type declaring both is still mutated.</summary>
    [Fact]
    public void A_type_that_declares_both_operators_is_mutated()
    {
        IReadOnlyList<MutationCandidate> candidates = MutateSource(
            "public struct Counter { public int Value; " +
            "public static Counter operator ++(Counter c) { c.Value = c.Value + 1; return c; } " +
            "public static Counter operator --(Counter c) { c.Value = c.Value - 1; return c; } } " +
            "public class C { public void M(Counter c) { c++; } }");

        Assert.Equal(["c--"], candidates.Select(candidate => candidate.Replacement.ToString()));
    }

    [Fact]
    public void The_mutations_are_attributed_to_this_family()
    {
        Assert.All(
            Candidates("x++"),
            candidate => Assert.Equal("Increment", candidate.Mutator.ToString()));
    }

    /// <summary>Runs the mutator over a whole compilation unit, for the user-defined operator cases.</summary>
    private static IReadOnlyList<MutationCandidate> MutateSource(string source)
    {
        (SyntaxTree tree, SemanticModel model) = TestCompilation.WithModel(source);

        return [.. tree.GetRoot().DescendantNodes().SelectMany(node => Mutator.Mutate(node, model))];
    }

    private static IReadOnlyList<MutationCandidate> Candidates(string statement) =>
        MutatorTestBase.MutateAll(Mutator, "int x", "void M({0}) { " + statement + "; }");

    private static IReadOnlyList<string> Mutate(string statement) =>
        [.. Candidates(statement).Select(candidate => candidate.Replacement.ToString())];
}
