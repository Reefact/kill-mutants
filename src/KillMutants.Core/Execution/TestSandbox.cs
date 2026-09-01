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

    private TestSandbox(string root, IReadOnlyList<TestProject> testProjects, IReadOnlyList<string> injectionPaths)
    {
        _root = root;
        _injectionPaths = injectionPaths;
        TestProjects = testProjects;
    }

    /// <summary>The target's test projects, rebased onto this sandbox's copies.</summary>
    public IReadOnlyList<TestProject> TestProjects { get; }

    /// <summary>This sandbox's private directory, for files that must not be shared with a sibling.</summary>
    public string Root => _root;

    /// <summary>Copies every output directory the target needs into a private location.</summary>
    public static TestSandbox CreateFor(MutationTestTarget target, string root)
    {
        ArgumentNullException.ThrowIfNull(target);

        List<TestProject> rebased = [];
        List<string> injectionPaths = [];

        foreach (TestProject testProject in target.TestProjects)
        {
            // Several test projects can share a name only if they sit in different directories, so
            // the index keeps the copies apart without needing the full path.
            string destination = Path.Combine(
                root,
                $"{testProject.Name}-{rebased.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

            CopyDirectory(testProject.OutputDirectory, destination);

            rebased.Add(new TestProject(
                testProject.ProjectPath,
                Path.Combine(destination, Path.GetFileName(testProject.AssemblyPath)),
                destination));

            injectionPaths.Add(Path.Combine(destination, target.ProjectUnderTest.AssemblyFileName));
        }

        return new TestSandbox(root, rebased, injectionPaths);
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
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temporary directory is not worth failing a run over.
        }
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
