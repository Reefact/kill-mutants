using KillMutants.Filtering;
using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;

namespace KillMutants.Core.Tests.Filtering;

/// <summary>
/// DEC0026: the calls a project does not hear about, recognised by the symbol they bind to, and what
/// a recognised call silences - its statement and its arguments, never the code handed to it.
/// </summary>
public class IgnoredCallsTests
{
    private static IReadOnlyList<Mutant> Generate(string source, params string[] rules) =>
        new MutantGenerator(MutatorCatalog.Default, ignoredCalls: IgnoredCalls.Of(rules))
            .Generate(TestCompilation.From(source));

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("*")]
    [InlineData("*.*")]
    [InlineData("**")]
    [InlineData("?*")]
    public void A_pattern_nobody_can_have_meant_is_refused(string rule)
    {
        Assert.Throws<ArgumentException>(() => IgnoredCalls.Validate([rule]));
    }

    [Theory]
    [InlineData("System.ArgumentNullException.ThrowIfNull")]
    [InlineData("*.ConfigureAwait")]
    [InlineData("System.ArgumentException.ThrowIf*")]
    [InlineData("ThrowIfNull")]
    public void A_pattern_that_names_something_is_accepted(string rule)
    {
        IgnoredCalls.Validate([rule]);
    }

    /// <summary>The statement of the slugger guards, silenced by the fully qualified name.</summary>
    [Fact]
    public void A_call_statement_is_silenced_by_the_name_of_the_method_it_binds_to()
    {
        IReadOnlyList<Mutant> mutants = Generate(
            "class C { void M(object x) { System.ArgumentNullException.ThrowIfNull(x); } }",
            "System.ArgumentNullException.ThrowIfNull");

        Mutant statement = Assert.Single(mutants, mutant => mutant.Mutator.ToString() == "Statement");

        Assert.Equal("System.ArgumentNullException.ThrowIfNull", statement.IgnoredBy);
    }

    /// <summary>#48: the mutant of ConfigureAwait's argument, through a generic Task.</summary>
    [Fact]
    public void An_argument_of_a_recognised_call_is_silenced()
    {
        IReadOnlyList<Mutant> mutants = Generate(
            "class C { async System.Threading.Tasks.Task<int> M(System.Threading.Tasks.Task<int> t) " +
            "=> await t.ConfigureAwait(false); }",
            "*.ConfigureAwait");

        Mutant literal = Assert.Single(mutants, mutant => mutant.Mutator.ToString() == "BooleanLiteral");

        Assert.Equal("*.ConfigureAwait", literal.IgnoredBy);
    }

    /// <summary>#50: a string handed to a static factory, silenced through a nested call too.</summary>
    [Fact]
    public void An_argument_of_a_call_nested_in_a_recognised_call_is_silenced()
    {
        IReadOnlyList<Mutant> mutants = Generate(
            "class C { string M(int a) => string.Concat(\"x\", System.Convert.ToString(a + 1)); }",
            "System.String.Concat");

        Assert.NotEmpty(mutants);
        Assert.All(mutants, mutant => Assert.Equal("System.String.Concat", mutant.IgnoredBy));
    }

    /// <summary>
    /// Code handed to a call runs later and its effects escape the call: a rule about the call does
    /// not reach the body of a lambda or of a local function passed to it.
    /// </summary>
    [Theory]
    [InlineData("class C { System.Collections.Generic.IEnumerable<int> M(System.Collections.Generic.IEnumerable<int> xs) " +
                "=> System.Linq.Enumerable.Where(xs, x => x >= 18); }")]
    [InlineData("class C { void M(System.Collections.Generic.List<System.Func<int, bool>> fs) " +
                "{ fs.Add(IsAdult); bool IsAdult(int x) => x >= 18; } }")]
    public void The_body_of_a_lambda_or_local_function_handed_to_a_call_is_not_silenced(string source)
    {
        IReadOnlyList<Mutant> mutants = Generate(source, "*.Where", "*.Add");

        Assert.Contains(mutants, mutant => mutant.OriginalText == "x >= 18" && mutant.IgnoredBy is null);
    }

    /// <summary>
    /// A bare name recognises nothing, so a wide net has to be written as one: the method of the
    /// project's own that shares a name with the guard is not the guard the project meant.
    /// </summary>
    [Fact]
    public void A_bare_name_recognises_nothing()
    {
        IReadOnlyList<Mutant> mutants = Generate(
            "class C { void M(object x) { System.ArgumentNullException.ThrowIfNull(x); } " +
            "static void ThrowIfNull(object o) { } void N(object y) { ThrowIfNull(y); } }",
            "ThrowIfNull");

        Assert.NotEmpty(mutants);
        Assert.All(mutants, mutant => Assert.Null(mutant.IgnoredBy));
    }

    [Fact]
    public void A_method_of_the_projects_own_that_shares_the_name_is_not_the_one_meant()
    {
        IReadOnlyList<Mutant> mutants = Generate(
            "class C { static void ThrowIfNull(object o) { } void N(object y) { ThrowIfNull(y); } }",
            "System.ArgumentNullException.ThrowIfNull");

        Mutant statement = Assert.Single(mutants, mutant => mutant.Mutator.ToString() == "Statement");

        Assert.Null(statement.IgnoredBy);
    }

    /// <summary>
    /// Review of #74. What a call is made on passes through it rather than being consumed by it:
    /// the value of <c>x + 1</c> is observed after <c>ConfigureAwait</c>, and a chain ending in an
    /// ignored <c>ToList</c> is not a chain the project asked to silence.
    /// </summary>
    [Theory]
    [InlineData("class C { async System.Threading.Tasks.Task<int> M(int x) " +
                "=> await System.Threading.Tasks.Task.FromResult(x + 1).ConfigureAwait(false); }",
                "*.ConfigureAwait", "x + 1")]
    [InlineData("using System.Linq; class C { System.Collections.Generic.List<int> M(System.Collections.Generic.IEnumerable<int> xs, int a) " +
                "=> xs.Take(a + 1).ToList(); }",
                "*.ToList", "a + 1")]
    public void What_a_call_is_made_on_is_not_silenced(string source, string rule, string heard)
    {
        IReadOnlyList<Mutant> mutants = Generate(source, rule);

        Assert.Contains(mutants, mutant => mutant.OriginalText == heard && mutant.IgnoredBy is null);
    }

    /// <summary>Review of #74: <c>base("…")</c> binds to the constructor <c>new</c> would.</summary>
    [Fact]
    public void A_constructor_initializer_is_recognised_as_the_constructor_it_calls()
    {
        IReadOnlyList<Mutant> mutants = Generate(
            "class E : System.ArgumentException { public E() : base(\"message\") { } }",
            "System.ArgumentException.ctor");

        Mutant literal = Assert.Single(mutants, mutant => mutant.Mutator.ToString() == "StringLiteral");

        Assert.Equal("System.ArgumentException.ctor", literal.IgnoredBy);
    }

    /// <summary>The message of a thrown exception, the most ordinary StringLiteral noise.</summary>
    [Fact]
    public void A_constructor_is_recognised_as_ctor()
    {
        IReadOnlyList<Mutant> mutants = Generate(
            "class C { void M() { throw new System.ArgumentException(\"message\"); } }",
            "System.ArgumentException.ctor");

        Mutant literal = Assert.Single(mutants, mutant => mutant.Mutator.ToString() == "StringLiteral");

        Assert.Equal("System.ArgumentException.ctor", literal.IgnoredBy);
    }

    [Fact]
    public void Case_is_the_languages()
    {
        IReadOnlyList<Mutant> mutants = Generate(
            "class C { void M(object x) { System.ArgumentNullException.ThrowIfNull(x); } }",
            "system.argumentnullexception.throwifnull");

        Assert.All(mutants, mutant => Assert.Null(mutant.IgnoredBy));
    }

    /// <summary>A silenced mutant is still generated, with the same key as it would have otherwise.</summary>
    [Fact]
    public void A_silenced_mutant_keeps_its_key()
    {
        const string source = "class C { void M(object x) { System.ArgumentNullException.ThrowIfNull(x); } }";

        Mutant heard = Assert.Single(Generate(source));
        Mutant silenced = Assert.Single(Generate(source, "*.ThrowIfNull"));

        Assert.Equal(heard.Key, silenced.Key);
        Assert.Null(heard.IgnoredBy);
        Assert.NotNull(silenced.IgnoredBy);
    }
}
