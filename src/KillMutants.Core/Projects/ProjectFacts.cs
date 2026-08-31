namespace KillMutants.Projects;

/// <summary>Everything KillMutants needs to know about one project, read in a single MSBuild call.</summary>
/// <param name="ProjectPath">Absolute path of the <c>.csproj</c>.</param>
/// <param name="AssemblyFileName">The file name of the assembly it produces.</param>
/// <param name="AssemblyPath">The absolute path of that assembly in the output directory.</param>
/// <param name="OutputDirectory">Where the project's build output lands.</param>
/// <param name="TargetFramework">The framework this query resolved to.</param>
/// <param name="TargetFrameworks">Every framework the project targets, empty when it targets one.</param>
/// <param name="PackageReferences">Package identifiers the project references.</param>
/// <param name="ProjectReferences">Absolute paths of the projects it references.</param>
internal sealed record ProjectFacts(
    string ProjectPath,
    string AssemblyFileName,
    string AssemblyPath,
    string OutputDirectory,
    string TargetFramework,
    IReadOnlyList<string> TargetFrameworks,
    IReadOnlyList<string> PackageReferences,
    IReadOnlyList<string> ProjectReferences)
{
    /// <summary>The project name, for display.</summary>
    public string Name => Path.GetFileNameWithoutExtension(ProjectPath);

    /// <summary>The directory containing the project file.</summary>
    public string Directory => Path.GetDirectoryName(ProjectPath)!;

    /// <summary>
    /// True when this project's tests decide whether a mutant is killed.
    /// </summary>
    /// <remarks>
    /// Recognised by its dependency on the xUnit 4 package family rather than by a naming
    /// convention: a project called <c>Foo.Tests</c> that references no test framework has no tests
    /// to run, and a differently named one that does is still a test project.
    /// </remarks>
    public bool IsTestProject =>
        PackageReferences.Any(package => package.StartsWith("xunit.v3", StringComparison.OrdinalIgnoreCase));

    /// <summary>True when the project targets more than one framework.</summary>
    public bool TargetsSeveralFrameworks => TargetFrameworks.Count > 1;
}
