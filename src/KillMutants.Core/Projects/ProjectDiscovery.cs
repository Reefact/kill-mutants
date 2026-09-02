using KillMutants.Filtering;
using KillMutants.Processes;
using KillMutants.Reporting;

namespace KillMutants.Projects;

/// <summary>
/// Finds the test projects in a directory tree and the projects they exercise.
/// </summary>
internal sealed class ProjectDiscovery
{
    private static readonly TimeSpan BuildBudget = TimeSpan.FromMinutes(10);

    private readonly HashSet<string> _leftOut = new(ProjectPaths.Comparer);
    private readonly MsBuildQuery _msBuild;
    private readonly string _configuration;
    private readonly PathFilter _exclusions;
    private readonly IProgress<MutationTestProgress>? _progress;

    public ProjectDiscovery(
        string configuration,
        PathFilter? exclusions = null,
        IProgress<MutationTestProgress>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration);

        _configuration = configuration;
        _exclusions = exclusions ?? PathFilter.None;
        _progress = progress;
        _msBuild = new MsBuildQuery(configuration);
    }

    /// <summary>
    /// Every test project the last discovery recognised, whether or not it exercises anything.
    /// </summary>
    /// <remarks>
    /// A test project that reaches no mutable project appears in no <see cref="MutationTestTarget"/>,
    /// because there is nothing to pair it with. That is right for a run and wrong for a partial one:
    /// a change can be what emptied it - remove a test project's last project reference and it
    /// exercises nothing at HEAD - and the selection has to recognise its files as test-side in order
    /// to ask the base revision what it used to cover. Reading the test projects back off the targets
    /// missed exactly that case, and an end-to-end test found it.
    /// </remarks>
    public IReadOnlyList<TestProject> TestProjects { get; private set; } = [];

    /// <summary>
    /// Projects a test project reaches that this run deliberately does not mutate.
    /// </summary>
    /// <remarks>
    /// Excluded by the user, or declaring themselves test support. Not a target, and not an accident
    /// either - which is the distinction a partial run needs when it asks whether a project has
    /// stopped being covered. "No test reaches it any more" and "you asked me to leave it alone" look
    /// identical from the target list and mean opposite things.
    /// </remarks>
    public IReadOnlySet<string> ProjectsLeftOut => _leftOut;

    /// <summary>
    /// Discovers everything to mutate under <paramref name="searchDirectory"/>.
    /// </summary>
    /// <remarks>
    /// Nothing is built here; the caller does that through <see cref="BuildTestProjectsAsync"/>.
    /// Keeping the two apart makes the ordering explicit, and the ordering matters: the build must
    /// precede every compiler-command-line query, and no MSBuild may run after injection.
    /// </remarks>
    public async Task<IReadOnlyList<MutationTestTarget>> DiscoverAsync(
        string searchDirectory,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ProjectFacts> projects = await ReadProjectsAsync(searchDirectory, cancellationToken)
            .ConfigureAwait(false);

        ProjectFacts[] testProjects = [.. projects.Where(project => project.IsTestProject)];

        if (testProjects.Length == 0)
        {
            throw new ProjectAnalysisException(
                $"No xUnit test project was found under '{searchDirectory}'. " +
                "KillMutants supports xUnit 4 - the xunit.v3 package family at version 4.");
        }

        RejectMultiTargetedTestProjects(testProjects);

        TestProjects =
        [
            .. testProjects.Select(test =>
                new TestProject(test.ProjectPath, test.AssemblyPath, test.OutputDirectory)),
        ];

        return await PairProjectsWithTheirTestsAsync(projects, testProjects, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Groups each mutable project with every test project that reaches it, directly or through
    /// another project.
    /// </summary>
    private async Task<IReadOnlyList<MutationTestTarget>> PairProjectsWithTheirTestsAsync(
        IReadOnlyList<ProjectFacts> projects,
        IReadOnlyList<ProjectFacts> testProjects,
        CancellationToken cancellationToken)
    {
        Dictionary<string, ProjectFacts> byPath = projects.ToDictionary(
            project => project.ProjectPath, ProjectPaths.Comparer);

        // Ordinal ordering keeps the report stable between runs, which matters as soon as there is
        // more than one project to report on.
        SortedDictionary<string, List<TestProject>> testsByProject = new(ProjectPaths.Comparer);

        // Every framework each mutable project is reached from, not just the first one seen. Keeping
        // only the first is what let two test suites on different frameworks share one target.
        Dictionary<string, SortedSet<string>> frameworksByProject = new(ProjectPaths.Comparer);

        // Filled only when a test project actually reaches an excluded project, so a repository
        // that excludes directories nothing references pays nothing for it.
        Dictionary<string, ProjectFacts?> beyondExclusions = new(ProjectPaths.Comparer);

        foreach (ProjectFacts testProject in testProjects)
        {
            var runnable = new TestProject(
                testProject.ProjectPath, testProject.AssemblyPath, testProject.OutputDirectory);

            IReadOnlyList<string> reachable = await ReachableProjectsAsync(
                    testProject, byPath, beyondExclusions, cancellationToken)
                .ConfigureAwait(false);

            foreach (string mutablePath in reachable)
            {
                if (!testsByProject.TryGetValue(mutablePath, out List<TestProject>? tests))
                {
                    testsByProject[mutablePath] = tests = [];
                    frameworksByProject[mutablePath] = new SortedSet<string>(StringComparer.Ordinal);
                }

                frameworksByProject[mutablePath].Add(testProject.TargetFramework);
                tests.Add(runnable);
            }
        }

        if (testsByProject.Count == 0)
        {
            throw new ProjectAnalysisException(
                "The test projects reference no other project, so there is nothing to mutate.");
        }

        RejectProjectsReachedFromSeveralFrameworks(frameworksByProject);

        List<MutationTestTarget> targets = [];

        foreach ((string mutablePath, List<TestProject> tests) in testsByProject)
        {
            // Resolved against the framework the test project actually loads. A project targeting
            // several would otherwise be analysed against an arbitrary one, and its mutants emitted
            // for a framework nothing runs.
            ProjectFacts facts = await _msBuild
                .GetProjectFactsAsync(
                    mutablePath, frameworksByProject[mutablePath].Single(), cancellationToken)
                .ConfigureAwait(false);

            targets.Add(new MutationTestTarget(
                new ProjectUnderTest(
                    facts.ProjectPath, facts.Directory, facts.AssemblyFileName, facts.TargetFramework),
                tests));
        }

        return targets;
    }

    /// <summary>
    /// Every non-test project a test project reaches, following project references transitively.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A test suite exercises the whole graph beneath it, not only what it names directly, and each
    /// of those assemblies sits in its output directory ready to be replaced. Other test projects
    /// are left out: their code is the yardstick, not the thing being measured.
    /// </para>
    /// <para>
    /// An excluded project is a hole in the graph, not a wall. Walking no further than one used to
    /// mean that <c>Tests -&gt; ExcludedFacade -&gt; Core</c> dropped <c>Core</c> along with the
    /// facade, and said nothing: the run reported on what was left. Excluding a project must stop it
    /// being mutated, never stop what sits behind it from being found.
    /// </para>
    /// <para>
    /// A project declaring itself test support is the same shape: skipped as a target, walked
    /// through as a graph. <c>Tests -&gt; TestSupport -&gt; Core</c> has to reach <c>Core</c>, or
    /// declaring the support library would cost the user the code it exists to test.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<string>> ReachableProjectsAsync(
        ProjectFacts testProject,
        Dictionary<string, ProjectFacts> byPath,
        Dictionary<string, ProjectFacts?> beyondExclusions,
        CancellationToken cancellationToken)
    {
        List<string> reachable = [];
        HashSet<string> seen = new(ProjectPaths.Comparer);
        Queue<string> pending = new(testProject.ProjectReferences);

        while (pending.Count > 0)
        {
            string path = pending.Dequeue();

            if (!seen.Add(path))
            {
                continue;
            }

            bool mutable = byPath.TryGetValue(path, out ProjectFacts? facts);

            facts ??= await FactsOfExcludedAsync(path, beyondExclusions, cancellationToken)
                .ConfigureAwait(false);

            if (facts is null || facts.IsTestProject)
            {
                continue;
            }

            if (mutable && !facts.IsTestSupport)
            {
                reachable.Add(path);
            }
            else
            {
                // Reached, and left alone on purpose. Recorded so that a partial run can tell this
                // apart from a project whose last test reference a change has just removed.
                _leftOut.Add(path);
            }

            foreach (string reference in facts.ProjectReferences)
            {
                pending.Enqueue(reference);
            }
        }

        return reachable;
    }

    /// <summary>
    /// Reads an excluded project's references so the traversal can pass through it, or null when the
    /// path is not an excluded project of this run.
    /// </summary>
    /// <remarks>
    /// No framework is named: only the references and the "is this a test project" answer are
    /// needed, and neither depends on which framework the project is evaluated for.
    /// </remarks>
    private async Task<ProjectFacts?> FactsOfExcludedAsync(
        string path,
        Dictionary<string, ProjectFacts?> cache,
        CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(path, out ProjectFacts? known))
        {
            return known;
        }

        ProjectFacts? facts = _exclusions.Excludes(path) && File.Exists(path)
            ? await _msBuild.GetProjectFactsAsync(path, cancellationToken: cancellationToken)
                .ConfigureAwait(false)
            : null;

        cache[path] = facts;

        return facts;
    }

    private async Task<IReadOnlyList<ProjectFacts>> ReadProjectsAsync(
        string searchDirectory,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(searchDirectory))
        {
            throw new ProjectAnalysisException($"'{searchDirectory}' is not a directory.");
        }

        string[] found = [.. Directory
            .EnumerateFiles(searchDirectory, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsUnderBuildOutput(path, searchDirectory))
            .Select(Path.GetFullPath)
            .Order(StringComparer.Ordinal)];

        if (found.Length == 0)
        {
            throw new ProjectAnalysisException($"No C# project was found under '{searchDirectory}'.");
        }

        string[] paths = [.. found.Where(path => !_exclusions.Excludes(path))];

        // Worth its own message: "no project found" would send the user looking at the directory
        // they gave rather than at the pattern they wrote.
        if (paths.Length == 0)
        {
            throw new ProjectAnalysisException(
                $"Every C# project under '{searchDirectory}' was excluded, so there is nothing to do.");
        }

        List<ProjectFacts> projects = [];

        foreach (string path in paths)
        {
            _progress?.Report(new MutationTestProgress(
                MutationTestPhase.Discovering, projects.Count, paths.Length,
                Path.GetFileNameWithoutExtension(path)));

            projects.Add(await _msBuild.GetProjectFactsAsync(path, cancellationToken: cancellationToken)
                .ConfigureAwait(false));
        }

        return projects;
    }

    /// <summary>
    /// The same rule as <see cref="RejectMultiTargetedTestProjects"/>, seen from the other end: a
    /// project reached by test suites on different frameworks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only the first framework used to be kept, and both suites were attached to that one target.
    /// The run then compiled one variant of the library and injected it into both test outputs -
    /// so one suite was measured against an assembly it does not reference, and its verdicts
    /// described a build that does not exist.
    /// </para>
    /// <para>
    /// Mutating the library once per framework is the other answer, and it is not this one. It would
    /// double the work, and it would collide on identity: a mutant's key is its file, position,
    /// rule and text, none of which say which framework it was built for, so the two variants of
    /// every mutant would be indistinguishable in the report they exist to be discussed in.
    /// </para>
    /// </remarks>
    internal static void RejectProjectsReachedFromSeveralFrameworks(
        IReadOnlyDictionary<string, SortedSet<string>> frameworksByProject)
    {
        KeyValuePair<string, SortedSet<string>>[] shared = [.. frameworksByProject
            .Where(entry => entry.Value.Count > 1)
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)];

        if (shared.Length == 0)
        {
            return;
        }

        string details = string.Join(
            Environment.NewLine,
            shared.Select(entry =>
                $"  {Path.GetFileNameWithoutExtension(entry.Key)}: reached from " +
                string.Join(", ", entry.Value)));

        throw new ProjectAnalysisException(
            "KillMutants cannot yet mutate a project that its test suites reach from different " +
            "frameworks, because each framework would need its own run and its own score." +
            Environment.NewLine + details);
    }

    /// <summary>
    /// A test project targeting several frameworks would need one run per framework, with a
    /// separate build output and a separate verdict for each. Rather than silently picking one and
    /// reporting a score for a framework the user did not choose, say so.
    /// </summary>
    private static void RejectMultiTargetedTestProjects(IReadOnlyList<ProjectFacts> testProjects)
    {
        ProjectFacts[] multiTargeted = [.. testProjects.Where(project => project.TargetsSeveralFrameworks)];

        if (multiTargeted.Length == 0)
        {
            return;
        }

        string details = string.Join(
            Environment.NewLine,
            multiTargeted.Select(project =>
                $"  {project.Name}: {string.Join(", ", project.TargetFrameworks)}"));

        throw new ProjectAnalysisException(
            "KillMutants cannot yet run a test project that targets several frameworks, because each " +
            "would need its own run and its own score." + Environment.NewLine + details);
    }

    /// <summary>
    /// Builds every test project, so each test application and its dependencies exist before any
    /// mutant is injected.
    /// </summary>
    /// <remarks>
    /// Must run before any compiler-command-line query, which relies on the intermediate assembly
    /// already existing, and long before injection: both <c>dotnet build</c> and <c>dotnet test</c>
    /// copy the pristine assembly back over a mutant.
    /// </remarks>
    public async Task BuildTestProjectsAsync(
        IEnumerable<MutationTestTarget> targets,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targets);

        string[] projectPaths = [.. targets
            .SelectMany(target => target.TestProjects)
            .Select(test => test.ProjectPath)
            // Identity by the filesystem's rule; order ordinal, so a report reads the same twice.
            .Distinct(ProjectPaths.Comparer)
            .Order(StringComparer.Ordinal)];

        for (int index = 0; index < projectPaths.Length; index++)
        {
            _progress?.Report(new MutationTestProgress(
                MutationTestPhase.Building, index, projectPaths.Length,
                Path.GetFileNameWithoutExtension(projectPaths[index])));

            await BuildAsync(projectPaths[index], cancellationToken).ConfigureAwait(false);
        }

        VerifyTestFramework(targets);
    }

    /// <summary>
    /// Requires every test project to actually run on the xUnit version this tool supports.
    /// </summary>
    /// <remarks>
    /// Discovery recognises a test project by its dependency on the <c>xunit.v3</c> package family,
    /// which says nothing about the version. KillMutants states xUnit 4 only, so it has to check
    /// rather than announce it - the runner's command line, its result file and its exit codes are
    /// all version-specific, and reading an older runner's output with these assumptions is how a
    /// tool comes to report verdicts it never measured.
    /// </remarks>
    private static void VerifyTestFramework(IEnumerable<MutationTestTarget> targets)
    {
        foreach (TestProject testProject in targets
                     .SelectMany(target => target.TestProjects)
                     .DistinctBy(test => test.ProjectPath, ProjectPaths.Comparer)
                     .OrderBy(test => test.ProjectPath, StringComparer.Ordinal))
        {
            if (XUnitVersion.WhyUnsupported(Path.GetDirectoryName(testProject.AssemblyPath)!) is { } reason)
            {
                throw new ProjectAnalysisException($"'{testProject.Name}' cannot be used: {reason}");
            }
        }
    }

    private async Task BuildAsync(string projectPath, CancellationToken cancellationToken)
    {
        ProcessResult result = await ProcessRunner.RunAsync(
                "dotnet",
                ["build", projectPath, "-c", _configuration, "-nologo"],
                Path.GetDirectoryName(projectPath)!,
                BuildBudget,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            throw new ProjectAnalysisException(
                $"'{Path.GetFileNameWithoutExtension(projectPath)}' failed to build, so there is " +
                $"nothing to mutate.{Environment.NewLine}{result.CombinedOutput}");
        }
    }

    private static bool IsUnderBuildOutput(string path, string root)
    {
        string relative = Path.GetRelativePath(root, path);

        return relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment =>
                segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("obj", StringComparison.OrdinalIgnoreCase));
    }
}
