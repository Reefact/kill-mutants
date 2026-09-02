namespace KillMutants.Projects;

/// <summary>Everything KillMutants needs to know about one project, read in a single MSBuild call.</summary>
/// <param name="ProjectPath">Absolute path of the <c>.csproj</c>.</param>
/// <param name="AssemblyFileName">The file name of the assembly it produces.</param>
/// <param name="AssemblyPath">The absolute path of that assembly in the output directory.</param>
/// <param name="OutputDirectory">Where the project's build output lands.</param>
/// <param name="TargetFramework">The framework this query resolved to.</param>
/// <param name="TargetFrameworks">Every framework the project targets, empty when it targets one.</param>
/// <param name="OutputType">The project's <c>OutputType</c>: <c>Exe</c>, <c>Library</c>, and so on.</param>
/// <param name="DeclaredTestSupport">Whether the project sets <c>KillMutantsTestSupport</c>.</param>
/// <param name="PackageReferences">Package identifiers the project references.</param>
/// <param name="ProjectReferences">Absolute paths of the projects it references.</param>
internal sealed record ProjectFacts(
    string ProjectPath,
    string AssemblyFileName,
    string AssemblyPath,
    string OutputDirectory,
    string TargetFramework,
    IReadOnlyList<string> TargetFrameworks,
    string OutputType,
    bool DeclaredTestSupport,
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
    /// <para>
    /// Recognised by what it is rather than by what it is called: a project named <c>Foo.Tests</c>
    /// that references no test framework has no tests to run, and a differently named one that does
    /// is still a test project.
    /// </para>
    /// <para>
    /// Two conditions, and the second is what a package reference alone gets wrong. xUnit v3 refuses
    /// to be referenced by a class library - "xUnit.net v3 test projects must be executable ... If
    /// this is not a test project, reference xunit.v3.extensibility.core instead" - so a helper
    /// library of builders and assertions has to reference <c>xunit.v3.assert</c> or
    /// <c>xunit.v3.extensibility.core</c>, both of which begin with <c>xunit.v3</c>. Matching on the
    /// prefix alone therefore called such a library a test project, and the run then tried to launch
    /// it: measured, it stopped with "could not find 'xunit.v3.core.dll'", and the production code
    /// behind that library was never mutated. Following xUnit's own instruction made a project
    /// unusable here.
    /// </para>
    /// <para>
    /// Requiring <c>Exe</c> is not a heuristic bolted on to patch that: it is the same rule xUnit
    /// enforces at build time, so a project this calls a test project is exactly one xUnit would let
    /// run.
    /// </para>
    /// </remarks>
    public bool IsTestProject =>
        string.Equals(OutputType, "Exe", StringComparison.OrdinalIgnoreCase) &&
        PackageReferences.Any(package => package.StartsWith("xunit.v3", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// True when the project is test scaffolding rather than code under test, and says so itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A library of builders, fakes and clocks is not the subject of a mutation run: mutating it
    /// reports findings about the test scaffolding, which nobody set out to measure. When it
    /// references xUnit, <see cref="IsTestProject"/> would once have caught it for the wrong reason
    /// and now correctly does not; when it references nothing in particular, no structural fact
    /// separates it from the code under test at all - which is why this is declared rather than
    /// inferred. See RB-025.
    /// </para>
    /// <para>
    /// Set <c>&lt;KillMutantsTestSupport&gt;true&lt;/KillMutantsTestSupport&gt;</c> in the project.
    /// It suppresses the project as a target without hiding what sits behind it, exactly as an
    /// exclusion does.
    /// </para>
    /// </remarks>
    public bool IsTestSupport => DeclaredTestSupport;

    /// <summary>True when the project targets more than one framework.</summary>
    public bool TargetsSeveralFrameworks => TargetFrameworks.Count > 1;
}
