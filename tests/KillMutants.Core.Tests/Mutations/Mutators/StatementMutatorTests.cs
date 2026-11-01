using KillMutants.Mutations.Mutators;
using Microsoft.CodeAnalysis;

namespace KillMutants.Core.Tests.Mutations.Mutators;

/// <summary>
/// A statement that only calls something is removed, a <c>yield return</c> its iterator can lose
/// is removed, and nothing else is - DEC0020, #62.
/// </summary>
public class StatementMutatorTests
{
    private static readonly StatementMutator Mutator = new();

    [Theory]
    [InlineData("void M(System.Action f) { f(); }", "f();")]
    [InlineData("void M(System.Text.StringBuilder b) { b.Append('x'); }", "b.Append('x');")]
    [InlineData("void M(System.Text.StringBuilder? b) { b?.Clear(); }", "b?.Clear();")]
    [InlineData("async System.Threading.Tasks.Task M(System.Func<System.Threading.Tasks.Task> f) { await f(); }", "await f();")]
    [InlineData("void M() { new System.Text.StringBuilder(); }", "new System.Text.StringBuilder();")]
    public void A_call_statement_becomes_an_empty_block(string member, string removed)
    {
        Assert.Equal(
            [(removed, "{}")],
            MutatorTestBase.MutateAll(Mutator, "", member)
                .Select(candidate => (candidate.Original.ToString(), candidate.Replacement.ToString())));
    }

    /// <summary>
    /// A block rather than an empty statement, because <c>if (a) ;</c> draws CS0642 and a block is
    /// legal wherever the call stood.
    /// </summary>
    [Fact]
    public void The_body_of_an_if_without_braces_stays_a_statement()
    {
        Assert.Equal(["{}"], MutatorTestBase.MutatedTexts(Mutator, "", "void M(bool a, System.Action f) { if (a) f(); }"));
    }

    [Theory]
    [InlineData("int M(System.Func<int> f) { int x; x = f(); return x; }")]
    [InlineData("void M(int x) { x++; }")]
    [InlineData("void M(int x) { x += 2; }")]
    [InlineData("int M(string s) { int n; int.TryParse(s, out n); return n; }")]
    [InlineData("int M(System.Func<int, int> f) { int x; f(x = 1); return x; }")]
    [InlineData("void M(bool a) { System.Diagnostics.Debug.Assert(a); }")]
    [InlineData("int M(System.Func<int> f) { return f(); }")]
    [InlineData("void M() { throw new System.InvalidOperationException(); }")]
    [InlineData("System.Action M(System.Action f) => () => f();")]
    public void What_is_not_a_plain_call_statement_is_left_alone(string member)
    {
        Assert.Empty(MutatorTestBase.MutateAll(Mutator, "", member));
    }

    /// <summary>
    /// Calls the compiler drops by itself, so removing them changes nothing and their mutant could
    /// never be killed: a <c>[Conditional]</c> method reached through <c>?.</c>, and a partial
    /// method nobody implemented.
    /// </summary>
    [Theory]
    [InlineData(
        "public class Log { [System.Diagnostics.Conditional(\"NEVER_DEFINED\")] public void Trace() { } } " +
        "public class C { public void M(Log? log) { log?.Trace(); } }")]
    [InlineData(
        "public partial class C { partial void OnChanged(); public void M() { OnChanged(); } }")]
    public void A_call_the_compiler_drops_is_left_alone(string source)
    {
        (SyntaxTree tree, SemanticModel model) = TestCompilation.WithModel(source);

        Assert.Empty(tree.GetRoot(TestContext.Current.CancellationToken)
            .DescendantNodes()
            .SelectMany(node => Mutator.Mutate(node, model)));
    }

    /// <summary>
    /// Each <c>yield return</c> of an iterator that has at least two is removed on its own, in a
    /// method, a property getter or a local function; a <c>yield break</c> never is.
    /// </summary>
    [Theory]
    [InlineData("System.Collections.Generic.IEnumerable<int> M() { yield return 1; yield return 2; }", "yield return 1;", "yield return 2;")]
    [InlineData("System.Collections.Generic.IEnumerable<int> M() { yield return 1; yield return 2; yield break; }", "yield return 1;", "yield return 2;")]
    [InlineData("System.Collections.Generic.IEnumerable<int> P { get { yield return 1; yield return 2; } }", "yield return 1;", "yield return 2;")]
    [InlineData("void M() { System.Collections.Generic.IEnumerable<int> L() { yield return 1; yield return 2; } }", "yield return 1;", "yield return 2;")]
    [InlineData("System.Collections.Generic.IEnumerable<int> M(bool a) { if (a) yield return 1; yield return 2; }", "yield return 1;", "yield return 2;")]
    [InlineData("System.Collections.Generic.IEnumerable<int> M(int[] a) { int i = 0; yield return a[i++]; yield return a[i]; }", "yield return a[i++];", "yield return a[i];")]
    public void A_yield_return_the_iterator_can_lose_becomes_an_empty_block(string member, string first, string second)
    {
        Assert.Equal(
            [(first, "{}"), (second, "{}")],
            MutatorTestBase.MutateAll(Mutator, "", member)
                .Select(candidate => (candidate.Original.ToString(), candidate.Replacement.ToString())));
    }

    /// <summary>
    /// The only <c>yield return</c> of an iterator is not proposed: without it the method is no
    /// iterator and returns nothing on its paths (CS0161). A <c>yield break</c> beside it does not
    /// change that, and the <c>yield</c>s of a nested local function are that function's, not its
    /// parent's - so the parent's single one stays and the local function's two are removed. Nor is
    /// a <c>yield return</c> that assigns a variable, with <c>=</c> or <c>out</c>: removing it would
    /// leave the variable unassigned where the next <c>yield</c> reads it (CS0165), which is why only
    /// that next one is proposed in the last two cases.
    /// </summary>
    [Theory]
    [InlineData("System.Collections.Generic.IEnumerable<int> M() { yield return 1; }", 0)]
    [InlineData("System.Collections.Generic.IEnumerable<int> M(bool a) { if (a) yield break; yield return 1; }", 0)]
    [InlineData("System.Collections.Generic.IEnumerable<int> M() { yield return 1; System.Collections.Generic.IEnumerable<int> L() { yield return 2; yield return 3; } }", 2)]
    [InlineData("System.Collections.Generic.IEnumerable<int> M() { int n; yield return n = 1; yield return n; }", 1)]
    [InlineData("System.Collections.Generic.IEnumerable<int> M(string s) { int n; yield return int.TryParse(s, out n) ? 1 : 0; yield return n; }", 1)]
    public void The_only_yield_return_of_an_iterator_is_left_alone(string member, int removed)
    {
        Assert.Equal(removed, MutatorTestBase.MutateAll(Mutator, "", member).Count);
    }

    /// <summary>An implemented partial method is called for real, so removing the call is a mutant.</summary>
    [Fact]
    public void A_call_to_an_implemented_partial_method_is_removed()
    {
        (SyntaxTree tree, SemanticModel model) = TestCompilation.WithModel(
            "public partial class C { partial void OnChanged(); partial void OnChanged() { } " +
            "public void M() { OnChanged(); } }");

        Assert.Single(tree.GetRoot(TestContext.Current.CancellationToken)
            .DescendantNodes()
            .SelectMany(node => Mutator.Mutate(node, model)));
    }
}
