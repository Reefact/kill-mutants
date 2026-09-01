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
            project => project.ProjectPath, StringComparer.Ordinal);

        // Ordinal ordering keeps the report stable between runs, which matters as soon as there is
        // more than one project to report on.
        SortedDictionary<string, List<TestProject>> testsByProject = new(StringComparer.Ordinal);
        Dictionary<string, string> frameworkByProject = new(StringComparer.Ordinal);

        foreach (ProjectFacts testProject in testProjects)
        {
            var runnable = new TestProject(
                testProject.ProjectPath, testProject.AssemblyPath, testProject.OutputDirectory);

            foreach (string mutablePath in ReachableProjects(testProject, byPath))
            {
                if (!testsByProject.TryGetValue(mutablePath, out List<TestProject>? tests))
                {
                    testsByProject[mutablePath] = tests = [];
                    frameworkByProject[mutablePath] = testProject.TargetFramework;
                }

                tests.Add(runnable);
            }
        }

        if (testsByProject.Count == 0)
        {
            throw new ProjectAnalysisException(
                "The test projects reference no other project, so there is nothing to mutate.");
        }

        List<MutationTestTarget> targets = [];

        foreach ((string mutablePath, List<TestProject> tests) in testsByProject)
        {
            // Resolved against the framework the test project actually loads. A project targeting
            // several would otherwise be analysed against an arbitrary one, and its mutants emitted
            // for a framework nothing runs.
            ProjectFacts facts = await _msBuild
                .GetProjectFactsAsync(mutablePath, frameworkByProject[mutablePath], cancellationToken)
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
    /// A test suite exercises the whole graph beneath it, not only what it names directly, and each
    /// of those assemblies sits in its output directory ready to be replaced. Other test projects
    /// are excluded: their code is the yardstick, not the thing being measured.
    /// </remarks>
    private static IEnumerable<string> ReachableProjects(
        ProjectFacts testProject,
        Dictionary<string, ProjectFacts> byPath)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        Queue<string> pending = new(testProject.ProjectReferences);

        while (pending.Count > 0)
        {
            string path = pending.Dequeue();

            if (!seen.Add(path) || !byPath.TryGetValue(path, out ProjectFacts? facts) || facts.IsTestProject)
            {
                continue;
            }

            yield return path;

            foreach (string reference in facts.ProjectReferences)
            {
                pending.Enqueue(reference);
            }
        }
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
            .Distinct(StringComparer.Ordinal)
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
                     .DistinctBy(test => test.ProjectPath, StringComparer.Ordinal)
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
