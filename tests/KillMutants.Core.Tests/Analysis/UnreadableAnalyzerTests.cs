using KillMutants.Analysis;

namespace KillMutants.Core.Tests.Analysis;

/// <summary>
/// An analyzer assembly that cannot be inspected is only a problem when it was going to contribute
/// code. Telling the two apart is what keeps this tool usable: refusing every unloadable analyzer
/// would turn away any project pinning a newer Roslyn for its linters, and refusing none of them
/// lets a run measure an assembly the build does not produce.
/// </summary>
/// <remarks>
/// The question is answered from the assembly's metadata rather than by loading it, which matters
/// because the assembly is in hand precisely because loading it failed. Nothing in it is executed.
/// </remarks>
[Collection(nameof(SerialFixtureAccess))]
public class UnreadableAnalyzerTests
{
    [Fact]
    public void An_assembly_carrying_a_generator_is_recognised_without_being_loaded()
    {
        Assert.True(SourceGenerators.CarriesAGeneratorForTests(GeneratorAssembly));
    }

    /// <summary>
    /// And its dependency, which is an ordinary library, is not - or the check would call every
    /// assembly a generator and refuse every project.
    /// </summary>
    [Fact]
    public void An_ordinary_assembly_is_not()
    {
        Assert.False(SourceGenerators.CarriesAGeneratorForTests(SupportAssembly));
    }

    /// <summary>
    /// A file that cannot be read at all answers null, which the caller treats as "yes". Refusing to
    /// guess in the safe direction is the posture, so it is pinned here rather than assumed.
    /// </summary>
    [Fact]
    public void A_file_that_is_not_an_assembly_refuses_to_guess()
    {
        string path = Path.Combine(Path.GetTempPath(), $"killmutants-test-{Guid.NewGuid():N}.dll");

        File.WriteAllText(path, "this is not a portable executable");

        try
        {
            Assert.Null(SourceGenerators.CarriesAGeneratorForTests(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string GeneratorAssembly { get; } = FixtureRepository.GeneratorAssembly;

    private static string SupportAssembly { get; } = FixtureRepository.GeneratorSupportAssembly;
}
