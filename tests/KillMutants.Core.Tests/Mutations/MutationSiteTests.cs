using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace KillMutants.Core.Tests.Mutations;

/// <summary>
/// C# copies compile-time constants into every call site. Mutating one changes the assembly under
/// test but not the already-compiled test assembly that reads it, so no test can possibly notice.
/// Such a mutant is guaranteed to survive however good the tests are, and reporting it would invent
/// a gap the user cannot act on.
/// </summary>
public class MutationSiteTests
{
    private static IReadOnlyList<Mutant> Generate(string source) =>
        new MutantGenerator(MutatorCatalog.Default).Generate(TestCompilation.From(source));

    [Theory]
    // const field: the value is inlined into every consumer at their build time
    [InlineData("class C { const bool Flag = 3 >= 2; }")]
    // const local: same rule inside a method body
    [InlineData("class C { void M() { const bool Flag = 3 >= 2; } }")]
    // default parameter value: copied into each call site
    [InlineData("class C { void M(bool flag = 3 >= 2) { } }")]
    // attribute argument: baked into metadata
    [InlineData("class C { [System.Obsolete(nameof(C) + \"\", 3 >= 2)] void M() { } }")]
    // enum member: a compile-time constant
    [InlineData("enum E { A = 3 >= 2 ? 1 : 0 }")]
    public void A_mutation_that_could_never_be_observed_is_not_generated(string source)
    {
        Assert.Empty(Generate(source));
    }

    /// <summary>
    /// Closes RB-016, found by running KillMutants on its own source. A pattern or `out` variable is
    /// definitely assigned only conditionally, and every mutation this tool makes to the expression
    /// that declares it changes when its parts are evaluated. `x is not T t || f(t)` turned into
    /// `x is not T t &amp;&amp; f(t)` leaves `t` unassigned; so does swapping the branches of
    /// `d.TryGetValue(k, out var v) ? v : 0`. Neither compiles, so neither is proposed.
    /// </summary>
    [Theory]
    // a pattern variable read after the guard: `||` into `&&` orphans it
    [InlineData("class C { int M(object o) { if (o is not string s || s.Length >= 3) { return 0; } return s.Length; } }",
                "s.Length >= 3")]
    // an `out` variable, the other half of the same problem
    [InlineData("class C { int M(System.Collections.Generic.Dictionary<int,int> d) " +
                "{ if (!d.TryGetValue(1, out int v) || v >= 3) { return 0; } return v; } }",
                "v >= 3")]
    public void An_expression_that_declares_a_variable_is_not_mutated(string source, string stillMutated)
    {
        IReadOnlyList<Mutant> mutants = Generate(source);

        // The declaring expression yields nothing...
        Assert.DoesNotContain(mutants, mutant => mutant.OriginalText.Contains("is not", StringComparison.Ordinal));
        Assert.DoesNotContain(mutants, mutant => mutant.OriginalText.Contains("TryGetValue", StringComparison.Ordinal));

        // ...while the ordinary expressions beside it still do. The rule must not swallow the file.
        Assert.Contains(mutants, mutant => mutant.OriginalText == stillMutated);
    }

    /// <summary>
    /// Regression test (RB-030). A variable declared on a line of its own escapes RB-016's rule, yet
    /// an <c>out</c> behind <c>&amp;&amp;</c> assigns it only when the left operand is true. The
    /// whole condition being true is what guarantees it is assigned where it is read, and every
    /// mutation of the condition, and the probe around it, takes that guarantee away.
    /// </summary>
    [Theory]
    [InlineData("class C { int M(string? s) { int y; if (s != null && int.TryParse(s, out y)) { return y; } return 0; } }",
                "s != null")]
    [InlineData("class C { int M(bool a, System.Func<int> f) { int y; if (a && (y = f()) > 0) { return y; } return 0; } }",
                "(y = f()) > 0")]
    public void An_expression_that_assigns_on_some_paths_only_is_not_mutated(string source, string stillMutated)
    {
        IReadOnlyList<Mutant> mutants = Generate(source);

        Assert.DoesNotContain(mutants, mutant => mutant.OriginalText.Contains("&&", StringComparison.Ordinal));

        // The operands still are: each assigns on every path it runs, or not at all.
        Assert.Contains(mutants, mutant => mutant.OriginalText == stillMutated);
    }

    /// <summary>
    /// The one boundary RB-016's rule stops at (#78). A variable declared inside a lambda is scoped
    /// to it, so the call around the lambda is mutated, and wrapped in the probe, without orphaning
    /// anything. The expression inside the lambda that declares it is refused as before.
    /// </summary>
    [Theory]
    // an `out` variable in a lambda: the comparison around the call is mutated, the `&&` that declares it is not
    [InlineData("class C { bool M(string[] xs, System.Collections.Generic.Dictionary<string,int> d) " +
                "=> System.Linq.Enumerable.Count(xs, m => d.TryGetValue(m, out int v) && v >= 1) >= 2; }",
                "System.Linq.Enumerable.Count(xs, m => d.TryGetValue(m, out int v) && v >= 1) >= 2",
                "&&")]
    // a pattern variable in a lambda, read by a ternary: the ternary is refused, the comparison around the call is not
    [InlineData("class C { bool M(object[] xs) " +
                "=> System.Linq.Enumerable.Sum(xs, o => o is string s ? s.Length : 0) >= 2; }",
                "System.Linq.Enumerable.Sum(xs, o => o is string s ? s.Length : 0) >= 2",
                "?")]
    public void A_declaration_inside_a_lambda_does_not_refuse_the_expression_around_it(
        string source, string mutated, string stillRefused)
    {
        IReadOnlyList<Mutant> mutants = Generate(source);

        Assert.Contains(mutants, mutant => mutant.OriginalText == mutated);
        Assert.DoesNotContain(mutants, mutant =>
            mutant.OriginalText != mutated && mutant.OriginalText.Contains(stillRefused, StringComparison.Ordinal));
    }

    /// <summary>
    /// RB-030's rule stops at the same boundary: an assignment inside a lambda never makes a
    /// variable definitely assigned outside it, so the <c>&amp;&amp;</c> that skips it is refused
    /// and the comparison around the call is not (#78).
    /// </summary>
    [Fact]
    public void An_assignment_inside_a_lambda_does_not_refuse_the_expression_around_it()
    {
        IReadOnlyList<Mutant> mutants = Generate(
            "class C { bool M(string[] xs) { int y = 0; " +
            "return System.Linq.Enumerable.Count(xs, m => m.Length >= 1 && (y = m.Length) > 0) >= 2; } }");

        Assert.Contains(mutants, mutant =>
            mutant.OriginalText == "System.Linq.Enumerable.Count(xs, m => m.Length >= 1 && (y = m.Length) > 0) >= 2");
        Assert.DoesNotContain(mutants, mutant => mutant.OriginalText == "m.Length >= 1 && (y = m.Length) > 0");
    }

    /// <summary>
    /// The line that found #78, with the family that has a mutant for it: on
    /// <c>Reefact/enum-member-name-binding</c> at <c>bc6eda9</c>, <c>Linq</c> made none here, over
    /// an <c>out</c> variable that could not reach the call.
    /// </summary>
    [Fact]
    public void A_linq_call_whose_lambda_declares_a_variable_is_mutated()
    {
        IReadOnlyList<Mutant> mutants = new MutantGenerator(MutatorCatalog.Of(added: [MutatorName.Create("Linq")]))
            .Generate(TestCompilation.From(
                "class C { System.Collections.Generic.IEnumerable<string> M(" +
                "string[] names, System.Collections.Generic.Dictionary<string, int> weights) " +
                "=> System.Linq.Enumerable.OrderByDescending(names, member => weights.TryGetValue(member, out int value) ? value : 0); }"),
                cancellationToken: TestContext.Current.CancellationToken);

        Mutant linq = Assert.Single(mutants, mutant => mutant.Mutator.ToString() == "Linq");
        Assert.StartsWith("System.Linq.Enumerable.OrderBy(", linq.MutatedText, StringComparison.Ordinal);
    }

    /// <summary>
    /// An <c>out</c> in a left operand runs every time, so the rule must leave its expression alone.
    /// </summary>
    [Fact]
    public void An_expression_that_assigns_on_every_path_is_still_mutated()
    {
        IReadOnlyList<Mutant> mutants = Generate(
            "class C { int M(string s, bool a) { int y; if (int.TryParse(s, out y) && a) { return y; } return 0; } }");

        Assert.Contains(mutants, mutant => mutant.OriginalText == "int.TryParse(s, out y) && a");
    }

    [Theory]
    // an ordinary expression-bodied member
    [InlineData("class C { bool M(int a) => a >= 2; }")]
    // a non-const field initialiser is evaluated at run time, so it IS observable
    [InlineData("class C { bool Flag = 3 >= 2; }")]
    // a static readonly field is also a run-time evaluation, unlike const
    [InlineData("class C { static readonly bool Flag = 3 >= 2; }")]
    // an ordinary local
    [InlineData("class C { void M() { var flag = 3 >= 2; } }")]
    public void An_observable_mutation_is_still_generated(string source)
    {
        Assert.NotEmpty(Generate(source));
    }

    [Fact]
    public void A_method_beside_a_constant_is_still_mutated()
    {
        // The constant must not shadow the rest of the type.
        IReadOnlyList<Mutant> mutants = Generate(
            "class C { const bool Flag = 3 >= 2; bool M(int a) => a >= 2; }");

        Assert.NotEmpty(mutants);
        Assert.All(mutants, mutant => Assert.Equal("a >= 2", mutant.OriginalText));
    }
}
