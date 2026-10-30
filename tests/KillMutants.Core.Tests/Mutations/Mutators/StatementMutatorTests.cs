using KillMutants.Mutations.Mutators;

namespace KillMutants.Core.Tests.Mutations.Mutators;

/// <summary>A statement that only calls something is removed, and nothing else is - DEC0020.</summary>
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
    [InlineData("void M(bool a) { System.Diagnostics.Debug.Assert(a); }")]
    [InlineData("int M(System.Func<int> f) { return f(); }")]
    [InlineData("void M() { throw new System.InvalidOperationException(); }")]
    [InlineData("System.Action M(System.Action f) => () => f();")]
    public void What_is_not_a_plain_call_statement_is_left_alone(string member)
    {
        Assert.Empty(MutatorTestBase.MutateAll(Mutator, "", member));
    }
}
