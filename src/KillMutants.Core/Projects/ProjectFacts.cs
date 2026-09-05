namespace KillMutants.Projects;

/// <summary>Everything KillMutants needs to know about one project, read in a single MSBuild call.</summary>
/// <param name="ProjectPath">Absolute path of the <c>.csproj</c>.</param>
/// <param name="AssemblyFileName">The file name of the assembly it produces.</param>
/// <param name="AssemblyPath">The absolute path of that assembly in the output directory.</param>
/// <param name="OutputDirectory">Where the project's build output lands.</param>
/// <param name="TargetFramework">The framework this query resolved to.</param>
/// <param name="TargetFrameworks">Every framework the project targets, empty when it targets one.</param>
/// <param name="OutputType">The project's <c>OutputType</c>: <c>Exe</c>, <c>Library</c>, and so on.</param>
/// <param name="XunitTestProject">Whether xUnit's own build files declared this a test project.</param>
/// <param name="DeclaredTestSupport">Whether the project sets <c>KillMutantsTestSupport</c>.</param>
/// <param name="PackageReferences">Package identifiers the project references.</param>
/// <param name="ProjectReferences">Absolute paths of the projects it references.</param>
/// <param name="AnalyzerProjects">
/// Absolute paths of the projects it references as analyzers - generators, which run at build time
/// and are deliberately absent from <paramref name="ProjectReferences"/> because nothing links them.
/// They still decide what this project compiles.
/// </param>
/// <param name="InputFiles">
/// Absolute paths of every file the project compiles or carries, empty unless the query was asked
/// for them. The authoritative answer to "does this project consume this file", which the directory
/// a file sits in only approximates.
/// </param>
internal sealed record ProjectFacts(
    string ProjectPath,
    string AssemblyFileName,
    string AssemblyPath,
    string OutputDirectory,
    string TargetFramework,
    IReadOnlyList<string> TargetFrameworks,
    string OutputType,
    bool XunitTestProject,
    bool DeclaredTestSupport,
    IReadOnlyList<string> PackageReferences,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<string> AnalyzerProjects,
    IReadOnlyList<string> InputFiles)
{
    /// <summary>The project name, for display.</summary>
    public string Name => Path.GetFileNameWithoutExtension(ProjectPath);

    /// <summary>The directory containing the project file.</summary>
    public string Directory => Path.GetDirectoryName(ProjectPath)!;

    /// <summary>
    /// The packages that make a project a test application, as opposed to merely referencing xUnit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Names, not a prefix. Every one of these depends on <c>xunit.v3.core.mtp-v2</c>, which is the
    /// package that generates the entry point and turns the project into something xUnit can run.
    /// Measured against the 4.0.0 packages by building an executable with a <c>Main</c> of its own
    /// against each: the three below fail with CS0017, "Program has more than one entry point
    /// defined", because xUnit had already contributed one; <c>xunit.v3.assert</c> and
    /// <c>xunit.v3.extensibility.core</c> build cleanly, because they contribute none.
    /// </para>
    /// <para>
    /// The list is short and it may be incomplete, which is a deliberate trade rather than an
    /// oversight. An unlisted flavour on a restored project is still recognised, because
    /// <see cref="XunitTestProject"/> answers first; on an unrestored one it is not, and the run
    /// stops with "no xUnit test project was found" - loud and wrong-looking, which is the failure
    /// this tool is allowed to have. The opposite mistake is the one it is not: calling something a
    /// test project when it is not makes it a wall in the reference graph, and the code behind it
    /// disappears from the run in silence.
    /// </para>
    /// </remarks>
    private static readonly string[] TestApplicationPackages =
    [
        "xunit.v3",
        "xunit.v3.core",
        "xunit.v3.mtp-v2",
        "xunit.v3.core.mtp-v2",
    ];

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
    /// Two conditions, and the second is asked in two ways because neither alone is available at the
    /// moment discovery needs it.
    /// </para>
    /// <para>
    /// <c>Exe</c> comes first, and it is xUnit's own rule rather than a heuristic: xUnit v3 refuses
    /// to be referenced by a class library - "xUnit.net v3 test projects must be executable (set
    /// project property <c>&lt;OutputType&gt;Exe&lt;/OutputType&gt;</c>). If this is not a test
    /// project, reference xunit.v3.extensibility.core instead" - so a helper library of builders and
    /// assertions has to reference <c>xunit.v3.assert</c> or <c>xunit.v3.extensibility.core</c>.
    /// Matching on the <c>xunit.v3</c> prefix alone therefore called such a library a test project,
    /// the run tried to launch it, and it stopped with "could not find 'xunit.v3.core.dll'". See
    /// RB-025.
    /// </para>
    /// <para>
    /// <c>Exe</c> is not enough on its own either, and the counter-example is not hypothetical: an
    /// executable referencing <c>xunit.v3.extensibility.core</c> builds happily with a <c>Main</c>
    /// of its own <em>and</em> has <c>xunit.v3.core.dll</c> in its output directory, so it passes
    /// both the prefix test and the runnability check this tool makes after the build - and would
    /// then be launched as a test application, run its own <c>Main</c>, and be reported as a suite
    /// that ran no tests. Worse, <see cref="ProjectDiscovery"/> stops walking at a test project, so
    /// such a tool sitting in <c>Tests -&gt; HelperExe -&gt; Core</c> would take <c>Core</c> out of
    /// the run without a word.
    /// </para>
    /// <para>
    /// <see cref="XunitTestProject"/> is xUnit's own answer, set as <c>XunitTestProject</c> in the
    /// <c>buildTransitive</c> props of <c>xunit.v3.core.mtp-v2</c>, and it is asked first because it
    /// covers packages this tool has never heard of. It arrives through NuGet's generated imports,
    /// so it is only there once the project has been restored - measured: <c>true</c> on a restored
    /// project referencing <c>xunit.v3.mtp-v2</c>, empty on the same project with its <c>obj</c>
    /// removed. Discovery runs before anything is built, so a cold clone would answer empty for
    /// every project and find no test project at all. <see cref="TestApplicationPackages"/> is what
    /// answers then.
    /// </para>
    /// <para>
    /// And a declaration outranks all of it. A project that says it is test scaffolding is not a test
    /// project of this run however it is built, so the two properties are mutually exclusive and no
    /// caller has to get their order right. Without that, <see cref="ProjectDiscovery"/> reaches its
    /// <see cref="IsTestProject"/> check first and stops walking there, so a declared helper that
    /// happens to be a runnable test application would be launched as a suite and would hide
    /// everything it references - the declaration silently ignored, which is the one thing a
    /// declaration must never be. Review found it.
    /// </para>
    /// <para>
    /// A user who marks their only test project as support gets "no xUnit test project was found",
    /// which is loud and points at the property. That is the right direction for this mistake to
    /// fail in.
    /// </para>
    /// </remarks>
    public bool IsTestProject =>
        !IsTestSupport &&
        string.Equals(OutputType, "Exe", StringComparison.OrdinalIgnoreCase) &&
        (XunitTestProject ||
         PackageReferences.Any(package =>
             TestApplicationPackages.Contains(package, StringComparer.OrdinalIgnoreCase)));

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
    /// exclusion does - and it is not run as a suite either, whatever the project is built as. See
    /// <see cref="IsTestProject"/>.
    /// </para>
    /// </remarks>
    public bool IsTestSupport => DeclaredTestSupport;

    /// <summary>True when the project targets more than one framework.</summary>
    public bool TargetsSeveralFrameworks => TargetFrameworks.Count > 1;
}
