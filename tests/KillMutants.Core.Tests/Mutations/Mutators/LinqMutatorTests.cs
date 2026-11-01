using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KillMutants.Core.Tests.Mutations.Mutators;

/// <summary>
/// A call is swapped for its counterpart when the counterpart is a twin of the method called: same
/// containing type, same return type, returned the same way - DEC0024.
/// </summary>
public class LinqMutatorTests
{
    private static readonly LinqMutator Mutator = new();

    private static IReadOnlyList<MutationCandidate> MutateAll(string source)
    {
        (SyntaxTree tree, SemanticModel model) = TestCompilation.WithModel(source);

        return [.. tree.GetRoot(TestContext.Current.CancellationToken)
            .DescendantNodes()
            .SelectMany(node => Mutator.Mutate(node, model))];
    }

    private static IReadOnlyList<string> Replacements(string source) =>
        [.. MutateAll(source).Select(candidate => candidate.Replacement.ToString())];

    private static string Member(string member) => $"using System.Linq; public class C {{ public {member} }}";

    /// <summary>Each of the eleven pairs, on the sequence types the pair is declared for, both ways.</summary>
    [Theory]
    [InlineData("bool M(int[] xs) => xs.Any(x => x > 0);", "xs.All(x => x > 0)")]
    [InlineData("bool M(int[] xs) => xs.All(x => x > 0);", "xs.Any(x => x > 0)")]
    [InlineData("int M(int[] xs) => xs.Min();", "xs.Max()")]
    [InlineData("int M(int[] xs) => xs.Max(x => x);", "xs.Min(x => x)")]
    [InlineData("int M(int[] xs) => xs.MinBy(x => x);", "xs.MaxBy(x => x)")]
    [InlineData("object M(int[] xs) => xs.OrderBy(x => x);", "xs.OrderByDescending(x => x)")]
    [InlineData("object M(IOrderedEnumerable<int> xs) => xs.ThenByDescending(x => x);", "xs.ThenBy(x => x)")]
    [InlineData("object M(int[] xs) => xs.Order();", "xs.OrderDescending()")]
    [InlineData("object M(int[] xs) => xs.Skip(1);", "xs.Take(1)")]
    [InlineData("object M(int[] xs) => xs.TakeWhile(x => x > 0);", "xs.SkipWhile(x => x > 0)")]
    [InlineData("object M(int[] xs) => xs.SkipLast(1);", "xs.TakeLast(1)")]
    [InlineData("object M(int[] xs, int[] ys) => xs.Union(ys);", "xs.Intersect(ys)")]
    [InlineData("object M(int[] xs, int[] ys) => xs.Except(ys);", "xs.Concat(ys)")]
    public void A_call_becomes_its_counterpart(string member, string replacement)
    {
        MutationCandidate candidate = Assert.Single(MutateAll(Member(member)));

        Assert.IsType<InvocationExpressionSyntax>(candidate.Original);
        Assert.IsType<InvocationExpressionSyntax>(candidate.Replacement);
        Assert.Equal(replacement, candidate.Replacement.ToString());
    }

    /// <summary>
    /// The rule names no class: the static form, an explicit type argument, a query, and an
    /// <c>ImmutableArray</c> - whose <c>Any</c> binds to <c>ImmutableArrayExtensions</c>, not to
    /// <c>Enumerable</c>, and is one of the two gaps that motivated the family - are all reached.
    /// </summary>
    [Theory]
    [InlineData("bool M(int[] xs) => Enumerable.Any(xs, x => x > 0);", "Enumerable.All(xs, x => x > 0)")]
    [InlineData("bool M(int[] xs) => xs.Any<int>(x => x > 0);", "xs.All<int>(x => x > 0)")]
    [InlineData("bool M(IQueryable<int> q) => q.Any(x => x > 0);", "q.All(x => x > 0)")]
    [InlineData("bool M(System.Collections.Immutable.ImmutableArray<int> xs) => xs.Any(x => x > 0);", "xs.All(x => x > 0)")]
    public void The_counterpart_is_found_wherever_a_library_declares_the_pair(string member, string replacement)
    {
        Assert.Equal([replacement], Replacements(Member(member)));
    }

    [Fact]
    public void An_immutable_array_binds_outside_Enumerable_and_is_still_reached()
    {
        (SyntaxTree tree, SemanticModel model) = TestCompilation.WithModel(
            Member("bool M(System.Collections.Immutable.ImmutableArray<int> xs) => xs.Any(x => x > 0);"));
        InvocationExpressionSyntax call = tree.GetRoot(TestContext.Current.CancellationToken)
            .DescendantNodes().OfType<InvocationExpressionSyntax>().Single();

        Assert.Equal(
            "System.Linq.ImmutableArrayExtensions",
            ((IMethodSymbol)model.GetSymbolInfo(call, TestContext.Current.CancellationToken).Symbol!)
                .ContainingType.ToDisplayString());
        Assert.Single(Mutator.Mutate(call, model));
    }

    /// <summary>
    /// Two pairs in one chain are two mutants, one per call, and each mutated node is the call it
    /// swaps: the inner one stops at the inner call.
    /// </summary>
    [Fact]
    public void Each_call_of_a_chain_is_swapped_on_its_own()
    {
        Assert.Equal(
            ["xs.OrderBy(x => x).ThenByDescending(x => x)", "xs.OrderByDescending(x => x)"],
            Replacements(Member("object M(int[] xs) => xs.OrderBy(x => x).ThenBy(x => x);")));
    }

    /// <summary>A user's own pair with the same shape is swapped: its author made them comparable.</summary>
    [Fact]
    public void A_user_type_that_declares_both_names_with_the_same_shape_is_swapped()
    {
        Assert.Equal(
            ["f.All(1)"],
            Replacements("public class F { public bool Any(int x) => x > 0; public bool All(int x) => x > 1; } " +
                         "public class C { public bool M(F f) => f.Any(1); }"));
    }

    /// <summary>
    /// What is left alone: a call with no counterpart overload, a user's <c>Any</c> whose type has no
    /// <c>All</c>, a counterpart on another type, one with another return type, one returned by
    /// <c>ref</c> where the original returns by value, and a call through <c>?.</c>.
    /// </summary>
    [Theory]
    [InlineData("using System.Linq; public class C { public bool M(int[] xs) => xs.Any(); }")]
    [InlineData("public class F { public bool Any() => true; } public class C { public bool M(F f) => f.Any(); }")]
    [InlineData("public class F { public bool Any() => true; } public static class E { public static bool All(this F f) => false; } " +
                "public class C { public bool M(F f) => f.Any(); }")]
    [InlineData("public class F { public int Min() => 0; public long Max() => 0; } public class C { public long M(F f) => f.Min(); }")]
    [InlineData("public class F { private bool b; public ref bool Any() => ref b; public bool All() => b; } " +
                "public class C { public bool M(F f) => f.Any(); }")]
    [InlineData("public class F { private bool b; public bool Any() => b; public ref readonly bool All() => ref b; } " +
                "public class C { public bool M(F f) => f.Any(); }")]
    [InlineData("using System.Linq; public class C { public bool? M(int[]? xs) => xs?.Any(x => x > 0); }")]
    public void What_has_no_twin_is_left_alone(string source)
    {
        Assert.Empty(MutateAll(source));
    }
}
