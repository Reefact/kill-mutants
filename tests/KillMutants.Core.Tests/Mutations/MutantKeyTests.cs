using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;

namespace KillMutants.Core.Tests.Mutations;

/// <summary>
/// Comparing two reports of the same commit - one from CI, one from a laptop - is the gesture that
/// catches a tool reporting a kill its suite does not produce. It needs an identity that survives
/// between runs, which a counter is not.
/// </summary>
public class MutantKeyTests
{
    private static readonly MutatorName Comparison = MutatorName.Create("Comparison");

    private static MutantKey Key(
        string path = "src/Ages.cs",
        int line = 3,
        int character = 12,
        string original = "age >= 18",
        string mutated = "age > 18") =>
        MutantKey.For(path, line, character, Comparison, original, mutated);

    [Fact]
    public void The_same_mutation_always_has_the_same_key()
    {
        Assert.Equal(Key(), Key());
    }

    [Theory]
    [InlineData("src/Other.cs", 3, 12, "age >= 18", "age > 18")]
    [InlineData("src/Ages.cs", 4, 12, "age >= 18", "age > 18")]
    [InlineData("src/Ages.cs", 3, 13, "age >= 18", "age > 18")]
    [InlineData("src/Ages.cs", 3, 12, "age >= 21", "age > 21")]
    // the same expression, mutated differently: the boundary shift and the negation
    [InlineData("src/Ages.cs", 3, 12, "age >= 18", "age < 18")]
    public void Any_difference_in_what_the_mutation_is_changes_the_key(
        string path, int line, int character, string original, string mutated)
    {
        Assert.NotEqual(Key(), Key(path, line, character, original, mutated));
    }

    [Fact]
    public void The_family_is_part_of_the_identity()
    {
        Assert.NotEqual(
            MutantKey.For("a.cs", 1, 1, Comparison, "a", "b"),
            MutantKey.For("a.cs", 1, 1, MutatorName.Create("Negation"), "a", "b"));
    }

    /// <summary>
    /// A separator between the parts, or `a.cs`+`bc` and `a.csb`+`c` would be the same mutation.
    /// </summary>
    [Fact]
    public void The_parts_cannot_run_together()
    {
        Assert.NotEqual(
            MutantKey.For("a.cs", 1, 1, Comparison, "x", "yz"),
            MutantKey.For("a.cs", 1, 1, Comparison, "x y", "z"));
    }

    /// <summary>
    /// Windows and Linux write the same path differently, and two reports that cannot be joined are
    /// two reports nobody compares.
    /// </summary>
    [Fact]
    public void A_path_is_the_same_path_whichever_separator_wrote_it()
    {
        Assert.Equal(Key("src/Ages.cs"), Key("src\\Ages.cs"));
    }

    /// <summary>
    /// The failure this whole type exists for. Narrowing a run renumbers every mutant after the
    /// first one dropped, so `M12` in one report is not `M12` in the next - while the key of a
    /// mutation that is still there does not move.
    /// </summary>
    [Fact]
    public void Narrowing_the_catalogue_moves_the_numbers_and_not_the_keys()
    {
        const string source =
            "class C { public string M(int a) { if (a >= 18) { return \"adult\"; } return \"minor\"; } }";

        IReadOnlyList<Mutant> everything = Generate(source, MutatorCatalog.Default);
        IReadOnlyList<Mutant> narrowed = Generate(
            source, MutatorCatalog.Of(unwanted: [MutatorName.Create("Comparison")]));

        Mutant[] shared =
        [
            .. everything.Where(mutant => narrowed.Any(other => other.Key == mutant.Key)),
        ];

        Assert.NotEmpty(shared);

        // Same mutations, same keys, different numbers - which is the whole point.
        Assert.All(shared, mutant => Assert.Single(narrowed, other => other.Key == mutant.Key));
        Assert.Contains(
            shared,
            mutant => narrowed.Single(other => other.Key == mutant.Key).Id != mutant.Id);
    }

    /// <summary>
    /// An absolute path differs between a runner and a container for reasons that have nothing to
    /// do with the mutation, so the key is built from the path relative to the run's root.
    /// </summary>
    [Fact]
    public void The_same_code_checked_out_somewhere_else_keeps_its_keys()
    {
        const string source = "class C { public bool M(int a) => a >= 18; }";

        Assert.Equal(
            Generate(source, MutatorCatalog.Default, "/runner/work/repo", "/runner/work/repo/src/Ages.cs")
                .Select(mutant => mutant.Key),
            Generate(source, MutatorCatalog.Default, "/home/dev/repo", "/home/dev/repo/src/Ages.cs")
                .Select(mutant => mutant.Key));
    }

    private static IReadOnlyList<Mutant> Generate(
        string source,
        MutatorCatalog catalog,
        string root = "/src",
        string path = "/src/Sample.cs") =>
        new MutantGenerator(catalog, exclusions: null, root)
            .Generate(TestCompilation.From(source, path));
}
