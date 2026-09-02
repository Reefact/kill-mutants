using KillMutants.Projects;
using KillMutants.Reporting;

namespace KillMutants.Selection;

/// <summary>
/// Which mutants a change puts in scope, per ADR-0010.
/// </summary>
/// <remarks>
/// <para>
/// Two halves. The changed production code is selected precisely, file by file, matched against the
/// syntax trees each compilation was actually built from. Anything touching an existing file in a
/// test project widens instead, to every mutable project that test project exercises - because what
/// a change to a test removes is a coverage <em>edge</em>, and HEAD cannot be asked about an edge
/// that is no longer there.
/// </para>
/// <para>
/// The relation is read at both revisions, <c>targets(base) ∪ targets(head)</c>. Reading HEAD alone
/// would let the same hole reappear one layer down: remove the <c>ProjectReference</c> from
/// <c>Tests</c> to <c>ProjectA</c> in the change being judged, and HEAD no longer says <c>Tests</c>
/// exercises <c>ProjectA</c>.
/// </para>
/// </remarks>
internal sealed class ChangeSelection
{
    /// <summary>
    /// Files outside a project directory that a build reads, and that change what the code does.
    /// </summary>
    /// <remarks>
    /// A change to one of these widens every mutable project beneath it, which in the usual case of
    /// a repository-root <c>Directory.Build.props</c> means all of them - a partial run that is
    /// briefly a full one. That is the honest answer: these files decide what is compiled, against
    /// which package versions, with which constants. Documentation, workflows and everything else
    /// outside a project are ignored, so a docs change selects nothing and finishes at once.
    /// </remarks>
    private static readonly string[] SharedBuildFiles =
    [
        "Directory.Build.props",
        "Directory.Build.targets",
        "Directory.Packages.props",
        "global.json",
        "nuget.config",
        "packages.lock.json",
    ];

    private readonly HashSet<string> _widened;
    private readonly MutantSelection _changedFiles;

    private ChangeSelection(RunScope scope, HashSet<string> widened, MutantSelection changedFiles)
    {
        Scope = scope;
        _widened = widened;
        _changedFiles = changedFiles;
    }

    /// <summary>What the report says about the population this run inspected.</summary>
    public RunScope Scope { get; }

    /// <summary>
    /// True when nothing in the change can produce a mutant, so there is nothing to build or run.
    /// </summary>
    /// <remarks>
    /// A conservative check, made before anything expensive: no project was widened and no C# file
    /// changed. A change that touches C# files belonging to no compilation still goes the long way
    /// round and finds no mutants, which is slower and never wrong.
    /// </remarks>
    public bool SelectsNothing => _widened.Count == 0 && _changedFiles.IsEmpty;

    /// <summary>What to generate mutants from, for one project under test.</summary>
    public MutantSelection For(ProjectUnderTest projectUnderTest)
    {
        ArgumentNullException.ThrowIfNull(projectUnderTest);

        return _widened.Contains(projectUnderTest.ProjectPath)
            ? MutantSelection.Everything
            : _changedFiles;
    }

    /// <summary>Reads the change and works out what it puts in scope.</summary>
    /// <param name="since">The revision to measure the change from.</param>
    /// <param name="searchDirectory">The directory the run was pointed at.</param>
    /// <param name="configuration">The build configuration, for reading the base revision's graph.</param>
    /// <param name="targets">What discovery found at HEAD.</param>
    /// <param name="testProjects">
    /// Every test project discovery recognised - not only those that appear in a target. A test
    /// project the change emptied exercises nothing at HEAD and is in no target, and its files still
    /// have to be recognised as test-side.
    /// </param>
    /// <param name="progress">Told when the base revision is being read, which is the slow part.</param>
    /// <param name="cancellationToken">Cancels the resolution.</param>
    /// <exception cref="ChangeSelectionException">
    /// The change, or the base revision's project graph, could not be read.
    /// </exception>
    public static async Task<ChangeSelection> ResolveAsync(
        string since,
        string searchDirectory,
        string configuration,
        IReadOnlyList<MutationTestTarget> targets,
        IReadOnlyList<TestProject> testProjects,
        IProgress<MutationTestProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(since);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(testProjects);

        GitRepository repository = await GitRepository
            .ContainingAsync(searchDirectory, cancellationToken)
            .ConfigureAwait(false);

        string head = await repository.ResolveAsync("HEAD", cancellationToken).ConfigureAwait(false);
        string named = await repository.ResolveAsync(since, cancellationToken).ConfigureAwait(false);
        string baseRevision = await repository.MergeBaseAsync(named, head, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<FileChange> changes = await repository
            .ChangesSinceAsync(baseRevision, cancellationToken)
            .ConfigureAwait(false);

        var scope = new RunScope(
            baseRevision,
            head,
            await repository.HasUncommittedChangesAsync(cancellationToken).ConfigureAwait(false),
            changes.Count);

        var resolver = new Resolver(
            repository, baseRevision, configuration, targets, testProjects, progress);

        return await resolver.ResolveAsync(scope, changes, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Everything the resolution needs to keep in hand while it classifies one change's files.
    /// </summary>
    private sealed class Resolver(
        GitRepository repository,
        string baseRevision,
        string configuration,
        IReadOnlyList<MutationTestTarget> targets,
        IReadOnlyList<TestProject> testProjects,
        IProgress<MutationTestProgress>? progress)
    {
        // From every test project discovery recognised, not from the targets. A test project that
        // exercises nothing at HEAD is in no target, and a change removing its last project
        // reference is precisely the case the base revision exists to answer.
        private readonly Dictionary<string, string> _testProjectByDirectory =
            testProjects
                .DistinctBy(test => test.ProjectPath, ProjectPaths.Comparer)
                .ToDictionary(
                    test => Path.GetDirectoryName(test.ProjectPath)!,
                    test => test.ProjectPath,
                    ProjectPaths.Comparer);

        private readonly Dictionary<string, ProjectUnderTest> _mutableByDirectory =
            targets.ToDictionary(
                target => target.ProjectUnderTest.ProjectDirectory,
                target => target.ProjectUnderTest,
                ProjectPaths.Comparer);

        public async Task<ChangeSelection> ResolveAsync(
            RunScope scope,
            IReadOnlyList<FileChange> changes,
            CancellationToken cancellationToken)
        {
            HashSet<string> widened = new(ProjectPaths.Comparer);
            HashSet<string> changedFiles = new(ProjectPaths.Comparer);

            // Test projects whose files the change touched, by the path git knows them under, so the
            // two revisions can be asked about the same project.
            HashSet<string> touchedTestProjects = new(ProjectPaths.Comparer);
            List<FileChange> unattributed = [];

            foreach (FileChange change in changes)
            {
                if (TestProjectOwning(change.Path) is { } testProject)
                {
                    // An added file cannot have removed a coverage edge that predates it, so it
                    // widens nothing. See ADR-0010, and the note there on what this implementation
                    // deliberately does not do in that case.
                    if (change.Kind != ChangeKind.Added)
                    {
                        touchedTestProjects.Add(testProject);
                    }

                    continue;
                }

                if (IsCSharp(change.Path))
                {
                    changedFiles.Add(change.Path);

                    continue;
                }

                if (MutableProjectOwning(change.Path) is { } mutable)
                {
                    // Not a source file, but inside a project: a project file, a resource, an input
                    // the code reads. Any of them can change what the assembly does or what it is
                    // built from, and none of them says which lines.
                    widened.Add(mutable.ProjectPath);

                    continue;
                }

                unattributed.Add(change);
            }

            foreach (FileChange change in unattributed)
            {
                if (IsSharedBuildFile(change.Path))
                {
                    WidenBeneath(widened, Path.GetDirectoryName(change.Path)!);
                }
            }

            await WidenForTestsAsync(widened, touchedTestProjects, unattributed, cancellationToken)
                .ConfigureAwait(false);

            return new ChangeSelection(scope, widened, MutantSelection.Of(changedFiles));
        }

        /// <summary>
        /// Widens to every project the touched test projects exercise, at both revisions.
        /// </summary>
        private async Task WidenForTestsAsync(
            HashSet<string> widened,
            HashSet<string> touchedTestProjects,
            IReadOnlyList<FileChange> unattributed,
            CancellationToken cancellationToken)
        {
            foreach (string testProject in touchedTestProjects)
            {
                foreach (MutationTestTarget target in targets.Where(target =>
                             target.TestProjects.Any(test =>
                                 ProjectPaths.Comparer.Equals(test.ProjectPath, testProject))))
                {
                    widened.Add(target.ProjectUnderTest.ProjectPath);
                }
            }

            // Files that belong to no project at HEAD may belong to one that the change deleted -
            // including a whole test project, which is the coverage edge vanishing one layer further
            // out again.
            IReadOnlyList<string> orphaned = await OrphanedProjectsAsync(unattributed, cancellationToken)
                .ConfigureAwait(false);

            if (touchedTestProjects.Count == 0 && orphaned.Count == 0)
            {
                return;
            }

            progress?.Report(new MutationTestProgress(
                MutationTestPhase.SelectingChanges, Subject: Short(baseRevision)));

            using BaseProjectGraph graph = await BaseProjectGraph
                .ExportAsync(repository, baseRevision, configuration, cancellationToken)
                .ConfigureAwait(false);

            List<string> atBase = [.. touchedTestProjects.Select(RepositoryPathOf).OfType<string>()];

            foreach (string candidate in orphaned)
            {
                if (await graph.IsTestProjectAsync(candidate, cancellationToken).ConfigureAwait(false))
                {
                    atBase.Add(candidate);
                }
            }

            foreach (string testProject in atBase.Distinct(RepositoryPath.Comparer))
            {
                foreach (string reached in await graph
                             .ProductionProjectsReachedFromAsync(testProject, cancellationToken)
                             .ConfigureAwait(false))
                {
                    // A project the base revision reached that this run does not mutate - excluded,
                    // deleted, or never a target - is not something the selection can widen to.
                    if (HeadTargetAt(reached) is { } target)
                    {
                        widened.Add(target);
                    }
                }
            }
        }

        /// <summary>
        /// Projects that existed at the base revision, own one of these files, and are gone at HEAD.
        /// </summary>
        private async Task<IReadOnlyList<string>> OrphanedProjectsAsync(
            IReadOnlyList<FileChange> unattributed,
            CancellationToken cancellationToken)
        {
            if (unattributed.Count == 0)
            {
                return [];
            }

            string[] projectsAtBase = [.. (await repository
                    .ListFilesAsync(baseRevision, cancellationToken).ConfigureAwait(false))
                .Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                .Where(path => !File.Exists(RepositoryPath.In(repository.Root, path)))];

            if (projectsAtBase.Length == 0)
            {
                return [];
            }

            HashSet<string> owning = new(RepositoryPath.Comparer);

            foreach (FileChange change in unattributed)
            {
                if (RepositoryPathOf(change.Path) is not { } relative)
                {
                    continue;
                }

                foreach (string project in projectsAtBase)
                {
                    if (RepositoryPath.IsUnder(relative, RepositoryPath.DirectoryOf(project)))
                    {
                        owning.Add(project);
                    }
                }
            }

            return [.. owning];
        }

        private string? HeadTargetAt(string repositoryPath)
        {
            string absolute = RepositoryPath.In(repository.Root, repositoryPath);

            return targets
                .Select(target => target.ProjectUnderTest.ProjectPath)
                .FirstOrDefault(path => ProjectPaths.Comparer.Equals(path, absolute));
        }

        private string? RepositoryPathOf(string path) => RepositoryPath.Of(repository.Root, path);

        private string? TestProjectOwning(string path) => Owning(_testProjectByDirectory, path);

        private ProjectUnderTest? MutableProjectOwning(string path) =>
            Owning(_mutableByDirectory, path);

        /// <summary>
        /// The nearest enclosing project, by directory, or null when the file is under none.
        /// </summary>
        /// <remarks>
        /// The longest match wins, because projects nest: a file under <c>src/Core/Sub</c> belongs to
        /// <c>src/Core/Sub</c> if there is a project there, and to <c>src/Core</c> otherwise.
        /// </remarks>
        private static T? Owning<T>(Dictionary<string, T> byDirectory, string path)
        {
            string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
            T? found = default;
            int longest = -1;

            foreach ((string candidate, T value) in byDirectory)
            {
                if (candidate.Length > longest && IsUnder(directory, candidate))
                {
                    found = value;
                    longest = candidate.Length;
                }
            }

            return found;
        }

        private void WidenBeneath(HashSet<string> widened, string directory)
        {
            foreach (MutationTestTarget target in targets)
            {
                if (IsUnder(target.ProjectUnderTest.ProjectDirectory, directory))
                {
                    widened.Add(target.ProjectUnderTest.ProjectPath);
                }
            }
        }

        /// <summary>
        /// True when <paramref name="directory"/> is <paramref name="ancestor"/> or sits beneath it.
        /// </summary>
        /// <remarks>
        /// Whole segments, and the filesystem's own rule for comparing them. A plain prefix test
        /// would put <c>src/CoreTests</c> inside <c>src/Core</c>, which is how a change to one
        /// project comes to be attributed to another.
        /// </remarks>
        private static bool IsUnder(string? directory, string ancestor)
        {
            if (directory is null)
            {
                return false;
            }

            if (ProjectPaths.Comparer.Equals(directory, ancestor))
            {
                return true;
            }

            string prefix = ancestor.EndsWith(Path.DirectorySeparatorChar)
                ? ancestor
                : ancestor + Path.DirectorySeparatorChar;

            return directory.Length > prefix.Length &&
                   ProjectPaths.Comparer.Equals(directory[..prefix.Length], prefix);
        }

        private static bool IsCSharp(string path) =>
            path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);

        private static bool IsSharedBuildFile(string path)
        {
            string name = Path.GetFileName(path);

            return SharedBuildFiles.Contains(name, StringComparer.OrdinalIgnoreCase) ||
                   name.EndsWith(".props", StringComparison.OrdinalIgnoreCase) ||
                   name.EndsWith(".targets", StringComparison.OrdinalIgnoreCase);
        }

        private static string Short(string revision) => revision.Length > 8 ? revision[..8] : revision;
    }
}
