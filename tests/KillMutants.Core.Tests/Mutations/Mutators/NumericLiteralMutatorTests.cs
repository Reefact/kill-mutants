using KillMutants.Mutations.Mutators;
using Microsoft.CodeAnalysis;

namespace KillMutants.Core.Tests.Mutations.Mutators;

/// <summary>
/// One step each way on an integer that names a value - DEC0019. Whether a <c>const</c> is reached
/// at all is the generator's decision, and is tested there.
/// </summary>
public class NumericLiteralMutatorTests
{
    private static readonly NumericLiteralMutator Mutator = new();

    [Theory]
    [InlineData("const int Limit = 100;")]
    [InlineData("static readonly int Limit = 100;")]
    [InlineData("int Limit = 100;")]
    [InlineData("int Limit { get; } = 100;")]
    public void A_named_value_moves_one_step_each_way(string member)
    {
        Assert.Equal(["101", "99"], MutatorTestBase.MutatedTexts(Mutator, "", member));
    }

    [Fact]
    public void A_constant_local_is_a_named_value_too()
    {
        Assert.Equal(
            ["4", "2"],
            MutatorTestBase.MutatedTexts(Mutator, "", "int M() { const int Tries = 3; return Tries; }"));
    }

    /// <summary>
    /// <c>0</c> and <c>1</c> are kept: in an initializer they are a value somebody named. Only the
    /// step below zero is refused, since a literal is never negative.
    /// </summary>
    [Fact]
    public void Zero_moves_up_only()
    {
        Assert.Equal(["1"], MutatorTestBase.MutatedTexts(Mutator, "", "const int None = 0;"));
    }

    [Fact]
    public void Every_literal_of_a_computed_value_is_moved()
    {
        Assert.Equal(
            ["201 * 200", "199 * 200", "200 * 201", "200 * 199"],
            [.. MutatorTestBase.MutateAll(Mutator, "", "const int Combinations = 200 * 200;")
                .Select(candidate => candidate.Original.Parent!.ReplaceNode(candidate.Original, candidate.Replacement).ToString())]);
    }

    [Fact]
    public void A_negative_value_moves_by_its_digits()
    {
        Assert.Equal(["6", "4"], MutatorTestBase.MutatedTexts(Mutator, "", "const int Floor = -5;"));
    }

    [Theory]
    [InlineData("const long Big = 100L;", "101L", "99L")]
    [InlineData("const long Floor = -2147483648u;", "2147483649u", "2147483647u")]
    [InlineData("const uint Mask = 0xFFu;", "256u", "254u")]
    [InlineData("const int Large = 40_000;", "40001", "39999")]
    public void The_suffix_and_so_the_type_are_kept(string member, string up, string down)
    {
        Assert.Equal([up, down], MutatorTestBase.MutatedTexts(Mutator, "", member));
    }

    /// <summary>A neighbour that no longer fits the type it is used as does not compile: CS0031.</summary>
    [Theory]
    [InlineData("const byte Full = 255;", "254")]
    [InlineData("const sbyte Lowest = -128;", "127")]
    [InlineData("const int Max = 2147483647;", "2147483646")]
    [InlineData("const int Min = -2147483648;", "2147483647")]
    [InlineData("const long Min = -9223372036854775808;", "9223372036854775807")]
    [InlineData("const long Min = -9223372036854775808L;", "9223372036854775807L")]
    public void A_neighbour_out_of_range_is_not_proposed(string member, string only)
    {
        Assert.Equal([only], MutatorTestBase.MutatedTexts(Mutator, "", member));
    }

    [Theory]
    [InlineData("int M(int x) => x + 100;")]
    [InlineData("int M(int limit = 100) => limit;")]
    [InlineData("int M() { int local = 100; return local; }")]
    [InlineData("System.Func<int> F = () => 100;")]
    [InlineData("const double Rate = 0.5;")]
    [InlineData("const decimal Fee = 5m;")]
    [InlineData("System.DayOfWeek Day = 0;")]
    public void What_is_not_an_integer_naming_a_value_is_left_alone(string member)
    {
        Assert.Empty(MutatorTestBase.MutateAll(Mutator, "", member));
    }
}
