using KillMutants.Projects;

namespace KillMutants.Execution;

/// <summary>
/// A private copy of a target's test output directories, into which one mutant at a time is
/// injected and its tests run.
/// </summary>
/// <remarks>
/// <para>
/// Concurrent mutants cannot share an output directory: each writes its own assembly over the same
/// file, so two workers would test each other's mutation. Giving every worker its own copy is what
/// makes running them in parallel possible at all.
/// </para>
/// <para>
/// It also means KillMutants stops writing into the developer's build output entirely. A run that
/// is killed halfway can no longer leave a mutated assembly in <c>bin</c>, which removes that
/// failure mode by construction rather than by remembering to clean up.
/// </para>
/// </remarks>
internal sealed class TestSandbox : IDisposable
{
    private readonly string _root;
    private readonly IReadOnlyList<string> _injectionPaths;
    private readonly IReadOnlyList<string> _copies;

    private TestSandbox(
        string root, IReadOnlyList<TestProject> testProjects, IReadOnlyList<string> injectionPaths, IReadOnlyList<string> copies)
    {
        _root = root;
        _injectionPaths = injectionPaths;
        _copies = copies;
        TestProjects = testProjects;
    }

    /// <summary>The target's test projects, rebased onto this sandbox's copies.</summary>
    public IReadOnlyList<TestProject> TestProjects { get; }

    /// <summary>This sandbox's private directory, for files that must not be shared with a sibling.</summary>
    public string Root => _root;

    /// <summary>Copies every output directory the target needs into a private location.</summary>
    /// <param name="target">The target whose test output is copied.</param>
    /// <param name="root">This sandbox's own directory, for the files no sibling may share.</param>
    /// <param name="projectRoots">
    /// Where each test project's copy goes, by its position in <see cref="MutationTestTarget.TestProjects"/>;
    /// <paramref name="root"/> for all of them when omitted.
    /// </param>
    /// <remarks>
    /// One directory per test project, because a copy keeps the files of the output it was taken
    /// from and not its ancestors: a suite that walks up from its own location has to be copied
    /// beside its own build output, not beside another suite's (#58, RB-032).
    /// </remarks>
    public static TestSandbox CreateFor(
        MutationTestTarget target, string root, IReadOnlyList<string>? projectRoots = null)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (projectRoots is not null && projectRoots.Count != target.TestProjects.Count)
        {
            throw new ArgumentException("One root is needed per test project.", nameof(projectRoots));
        }

        List<TestProject> rebased = [];
        List<string> injectionPaths = [];

        foreach (TestProject testProject in target.TestProjects)
        {
            // Several test projects can share a name only if they sit in different directories, so
            // the index keeps the copies apart without needing the full path.
            string destination = Path.Combine(
                projectRoots?[rebased.Count] ?? root,
                $"{testProject.Name}-{rebased.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

            CopyDirectory(testProject.OutputDirectory, destination);

            rebased.Add(new TestProject(
                testProject.ProjectPath,
                Path.Combine(destination, Path.GetFileName(testProject.AssemblyPath)),
                destination));

            injectionPaths.Add(Path.Combine(destination, target.ProjectUnderTest.AssemblyFileName));
        }

        return new TestSandbox(root, rebased, injectionPaths, [.. rebased.Select(project => project.OutputDirectory)]);
    }

    /// <summary>Writes an assembly over every copy this sandbox's test hosts will load.</summary>
    public void Inject(byte[] assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        foreach (string path in _injectionPaths)
        {
            File.WriteAllBytes(path, assembly);
        }
    }

    /// <summary>Deletes the copies.</summary>
    public void Dispose()
    {
        foreach (string copy in _copies)
        {
            Scratch.DeleteDirectory(copy);
        }

        Scratch.DeleteDirectory(_root);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (string file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }

        foreach (string directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }
}
