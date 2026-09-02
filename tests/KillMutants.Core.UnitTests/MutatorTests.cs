using KillMutants;
using Xunit;

namespace KillMutants.UnitTests;

public sealed class MutatorTests
{
    private static readonly Mutator Engine = new();

    [Theory]
    [InlineData("a + b", "arithmetic", "+", "-")]
    [InlineData("a - b", "arithmetic", "-", "+")]
    [InlineData("a * b", "arithmetic", "*", "/")]
    [InlineData("a / b", "arithmetic", "/", "*")]
    [InlineData("a % b", "arithmetic", "%", "*")]
    [InlineData("a == b", "equality", "==", "!=")]
    [InlineData("a != b", "equality", "!=", "==")]
    [InlineData("a && b", "logical", "&&", "||")]
    [InlineData("a || b", "logical", "||", "&&")]
    public void Swaps_a_binary_operator(string expression, string family, string original, string replacement)
    {
        var mutants = Engine.FindMutants(Wrap(expression));

        var mutant = Assert.Single(mutants);
        Assert.Equal(family, mutant.Operator);
        Assert.Equal(original, mutant.Original);
        Assert.Equal(replacement, mutant.Replacement);
    }

    [Fact]
    public void Offers_both_neighbours_of_a_relational_operator()
    {
        // `<` is wrong in two interesting ways: off by one (`<=`) and reversed (`>=`). A test suite
        // that catches one and not the other has a real gap, so both are worth producing.
        var mutants = Engine.FindMutants(Wrap("a < b"));

        Assert.Equal(["<=", ">="], mutants.Select(m => m.Replacement));
        Assert.All(mutants, m => Assert.Equal("relational", m.Operator));
    }

    [Theory]
    [InlineData("true", "false")]
    [InlineData("false", "true")]
    public void Swaps_a_boolean_literal(string literal, string replacement)
    {
        var mutants = Engine.FindMutants(Wrap(literal));

        var mutant = Assert.Single(mutants);
        Assert.Equal("boolean", mutant.Operator);
        Assert.Equal(replacement, mutant.Replacement);
    }

    [Fact]
    public void Reports_a_mutant_at_its_line_and_column()
    {
        const string source = """
            class C
            {
                int M(int a, int b) => a + b;
            }
            """;

        var mutant = Assert.Single(Engine.FindMutants(source));

        Assert.Equal(3, mutant.Line);
        Assert.Equal(30, mutant.Column);
    }

    [Fact]
    public void Applying_a_mutant_changes_only_its_own_characters()
    {
        const string source = "class C { int M(int a, int b) => a + b; }";

        var mutated = Assert.Single(Engine.FindMutants(source)).ApplyTo(source);

        Assert.Equal("class C { int M(int a, int b) => a - b; }", mutated);
    }

    [Fact]
    public void Finds_every_mutant_in_file_order()
    {
        const string source = """
            class C
            {
                bool M(int a, int b) => a > b && a == 0;
            }
            """;

        var mutants = Engine.FindMutants(source);

        // `>` gives two, `&&` one, `==` one: four in all, ordered as a reader meets them.
        Assert.Equal([">=", "<=", "||", "!="], mutants.Select(m => m.Replacement));
        Assert.Equal(mutants.OrderBy(m => m.Span.Start), mutants);
    }

    [Fact]
    public void Finds_nothing_in_a_file_with_no_mutable_construct()
    {
        Assert.Empty(Engine.FindMutants("class C { }"));
    }

    [Fact]
    public void Reads_a_file_that_does_not_compile()
    {
        // Parsing is not compiling. A file with a syntax error still has a tree, and the mutants in
        // the parts that did parse are still real — refusing to answer would make the engine useless
        // on exactly the code somebody is in the middle of fixing.
        var mutants = Engine.FindMutants("class C { int M() => 1 + ; }");

        Assert.Single(mutants);
    }

    private static string Wrap(string expression) => $"class C {{ object M(dynamic a, dynamic b) => {expression}; }}";
}
