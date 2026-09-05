using KillMutants.Projects;

namespace KillMutants.Core.Tests.Projects;

/// <summary>
/// What a project consumes includes the analyzers it names, and excludes the ones it did not.
/// </summary>
/// <remarks>
/// <para>
/// Review found the item missing. A generator referenced as a <em>project</em> has a relation of its
/// own, and a file a project compiles or carries was read from six item names - but a generator
/// shipped as a DLL beside the tree and consumed with
/// <c>&lt;Analyzer Include="../tools/Gen.dll" /&gt;</c> is neither. Sitting outside every project
/// directory, the directory rule could not claim it; belonging to no item, membership could not
/// either. A diff carrying only that file was attributed to nothing, and a partial run passed over
/// whatever the generator now emits differently.
/// </para>
/// <para>
/// Asked of evaluation rather than through a run, and the first attempt measured why. An end-to-end
/// test needs a file the item can name, and a placeholder is not enough: KillMutants refuses an
/// analyzer it cannot load (RB-017), so the run stops before attribution is ever consulted. That
/// refusal is right and is tested elsewhere. What is wrong here is what evaluation reports as an
/// input, which is exactly what this asks - and evaluation reads the item without opening the file.
/// </para>
/// </remarks>
public class AnalyzerInputTests
{
    [Fact]
    public async Task An_analyzer_the_project_names_is_one_of_its_inputs()
    {
        using var project = new TemporaryProject(
            """
            <ItemGroup><Analyzer Include="../tools/Gen.dll" /></ItemGroup>
            """);

        IReadOnlyList<string> inputs = await new MsBuildQuery("Debug", readInputFiles: true)
            .GetInputFilesAsync(project.ProjectPath, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(
            inputs,
            input => input.EndsWith(Path.Combine("tools", "Gen.dll"), StringComparison.Ordinal));
    }

    /// <summary>
    /// The SDK's own analyzers are not what this repository consumes, and must not be indexed.
    /// </summary>
    /// <remarks>
    /// Measured before the filter was written: <c>-getItem:Analyzer</c> answers with the item the
    /// project declared <em>and</em> the SDK's, from the install directory, marked
    /// <c>IsImplicitlyDefined</c>. Without the flag every project in a run would claim to consume
    /// them, and the index that answers "who consumes this file" would carry a machine's dotnet
    /// installation alongside the code. Measured on the six item names that were already read too -
    /// none carries the flag - so the filter takes away nothing that was there before.
    /// </remarks>
    [Fact]
    public async Task The_analyzers_the_sdk_supplies_are_not()
    {
        using var project = new TemporaryProject(
            """
            <ItemGroup><Analyzer Include="../tools/Gen.dll" /></ItemGroup>
            """);

        IReadOnlyList<string> inputs = await new MsBuildQuery("Debug", readInputFiles: true)
            .GetInputFilesAsync(project.ProjectPath, cancellationToken: TestContext.Current.CancellationToken);

        Assert.DoesNotContain(
            inputs,
            input => input.Contains(
                Path.Combine("Microsoft.NET.Sdk", "analyzers"), StringComparison.OrdinalIgnoreCase));
    }

    private sealed class TemporaryProject : IDisposable
    {
        private readonly string _root;

        public TemporaryProject(string extraItems)
        {
            _root = Path.Combine(Path.GetTempPath(), $"killmutants-analyzer-{Guid.NewGuid():N}");

            Directory.CreateDirectory(Path.Combine(_root, "Sample"));

            ProjectPath = Path.Combine(_root, "Sample", "Sample.csproj");

            File.WriteAllText(
                ProjectPath,
                $"""
                 <Project Sdk="Microsoft.NET.Sdk">
                   <PropertyGroup>
                     <TargetFramework>net10.0</TargetFramework>
                   </PropertyGroup>
                 {extraItems}
                 </Project>
                 """);

            File.WriteAllText(Path.Combine(_root, "Sample", "Code.cs"), "public class Code;");
        }

        public string ProjectPath { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temporary directory is not worth failing a test over.
            }
        }
    }
}
