using KillMutants.Analysis;
using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;
using KillMutants.Projects;
using Microsoft.CodeAnalysis.CSharp;

namespace KillMutants.Core.Tests.Analysis;

/// <summary>
/// Integration tests against the real fixture project: a real MSBuild query, a real Roslyn
/// compilation and a real emit. There are no mocks here on purpose - the whole risk of this layer
/// lies in whether the reconstructed compilation genuinely matches the one MSBuild would run.
/// </summary>
[Collection(nameof(SerialFixtureAccess))]
public class ProjectCompilationTests
{
    private static async Task<ProjectCompilation> CompileFixtureAsync()
    {
        var msBuild = new MsBuildQuery("Release");
        string project = FixtureRepository.SampleLibraryProject;

        IReadOnlyList<string> arguments = await msBuild.GetCscCommandLineAsync(project, TestContext.Current.CancellationToken);
        CSharpCommandLineArguments parsed = CscCommandLine.Parse(arguments, Path.GetDirectoryName(project)!);

        return ProjectCompilation.Create(parsed, Path.GetDirectoryName(project)!);
    }

    [Fact]
    public async Task The_compiler_command_line_describes_a_complete_compilation()
    {
        var msBuild = new MsBuildQuery("Release");
        string project = FixtureRepository.SampleLibraryProject;

        IReadOnlyList<string> arguments = await msBuild.GetCscCommandLineAsync(project, TestContext.Current.CancellationToken);
        CSharpCommandLineArguments parsed = CscCommandLine.Parse(arguments, Path.GetDirectoryName(project)!);

        Assert.Empty(parsed.Errors);
        Assert.NotEmpty(parsed.MetadataReferences);
        Assert.Equal(LanguageVersion.CSharp14, parsed.ParseOptions.LanguageVersion);

        // The generated sources must be present: dropping AssemblyInfo.cs sets the assembly version
        // to 0.0.0.0, the test host then fails to load the assembly, and that surfaces as an
        // ordinary test failure - a false kill.
        Assert.Contains(parsed.SourceFiles, file => file.Path.EndsWith("AssemblyInfo.cs", StringComparison.Ordinal));
        Assert.Contains(parsed.SourceFiles, file => file.Path.EndsWith("Ages.cs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_empty_command_line_is_rejected_rather_than_silently_compiling_nothing()
    {
        // Roslyn parses an empty command line into a perfectly valid, perfectly useless compilation.
        Assert.Throws<ProjectAnalysisException>(() => CscCommandLine.Parse([], "/tmp"));

        // A command line missing /out: or /target: is equally unusable.
        Assert.Throws<ProjectAnalysisException>(() => CscCommandLine.Parse(["/nologo"], "/tmp"));

        await Task.CompletedTask;
    }

    [Fact]
    public async Task The_unmutated_compilation_emits_successfully()
    {
        ProjectCompilation compilation = await CompileFixtureAsync();

        EmitOutcome outcome = compilation.EmitBaseline();

        Assert.True(outcome.Success, outcome.Diagnostics);
        Assert.NotNull(outcome.Assembly);
        Assert.NotEmpty(outcome.Assembly);
    }

    /// <summary>
    /// The decisive test for this layer. A mutant whose emitted bytes match the baseline is
    /// equivalent to the original and can never be killed - which is exactly what a token-level
    /// rewrite silently produces.
    /// </summary>
    [Fact]
    public async Task A_mutant_emits_an_assembly_that_actually_differs_from_the_baseline()
    {
        ProjectCompilation compilation = await CompileFixtureAsync();
        IReadOnlyList<Mutant> mutants = new MutantGenerator(MutatorCatalog.Default)
            .Generate(compilation.SyntaxTrees);

        Mutant mutant = Assert.Single(mutants);
        Assert.Equal("age >= 18", mutant.OriginalText);

        EmitOutcome baseline = compilation.EmitBaseline();
        EmitOutcome mutated = compilation.EmitWith(mutant);

        Assert.True(mutated.Success, mutated.Diagnostics);
        Assert.NotEqual(baseline.Assembly, mutated.Assembly);
    }
}

/// <summary>The fixture projects are shared state on disk, so tests touching them run one at a time.</summary>
[CollectionDefinition(nameof(SerialFixtureAccess), DisableParallelization = true)]
public class SerialFixtureAccess;
