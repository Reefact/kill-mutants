using KillMutants.Projects;
using KillMutants.Reporting;

namespace KillMutants.Selection;

/// <summary>
/// Which mutants a change puts in scope, per DEC0011.
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
        /// <summary>
        /// Test projects by the directory they sit in, all of them, and from every test project
        /// discovery recognised rather than from the targets.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A list per directory, not one project: two projects in one folder is unusual and legal,
        /// and review found that assuming otherwise threw a duplicate-key exception before a single
        /// change had been classified - so every partial run in such a repository died where a full
        /// run works, which is the worst shape a limitation can take.
        /// </para>
        /// <para>
        /// From <see cref="ProjectDiscovery.TestProjects"/>, because a test project that exercises
        /// nothing at HEAD is in no target - and a change that emptied it is exactly the case the
        /// base revision exists to answer.
        /// </para>
        /// </remarks>
        private readonly Dictionary<string, List<string>> _testProjectsByDirectory =
            Grouped(testProjects.DistinctBy(test => test.ProjectPath, ProjectPaths.Comparer),
                test => Path.GetDirectoryName(test.ProjectPath)!,
                test => test.ProjectPath);

        private readonly Dictionary<string, List<ProjectUnderTest>> _mutableProjectsByDirectory =
            Grouped(
                targets,
                target => target.ProjectUnderTest.ProjectDirectory,
                target => target.ProjectUnderTest);

        /// <summary>
        /// Every file each test project compiles or carries, or null until a change needs asking.
        /// </summary>
        private Dictionary<string, HashSet<string>>? _testProjectInputs;

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
                // A directory, not a file: git reports a submodule bump as its gitlink path alone -
                // measured, `M libs/Core` and nothing beneath it. Everything after this reads the
                // path's parent directory, which for a directory is the wrong project entirely, so
                // the whole subtree is taken conservatively before anything else looks at it.
                if (Directory.Exists(change.Path))
                {
                    WidenBeneath(widened, change.Path);

                    foreach (string testProject in TestProjectsBeneath(change.Path))
                    {
                        touchedTestProjects.Add(testProject);
                    }

                    continue;
                }

                IReadOnlyList<string> owningTests = await TestProjectsOwningAsync(
                        change.Path, cancellationToken)
                    .ConfigureAwait(false);

                if (owningTests.Count > 0)
                {
                    // An added file cannot have removed a coverage edge that predates it, so it
                    // widens nothing. See DEC0011, and the note there on what this implementation
                    // deliberately does not do in that case.
                    if (change.Kind != ChangeKind.Added)
                    {
                        foreach (string testProject in owningTests)
                        {
                            touchedTestProjects.Add(testProject);
                        }
                    }

                    continue;
                }

                if (IsCSharp(change.Path))
                {
                    changedFiles.Add(change.Path);

                    continue;
                }

                List<ProjectUnderTest> mutable = MutableProjectsOwning(change.Path);

                if (mutable.Count > 0)
                {
                    // Not a source file, but inside a project: a project file, a resource, an input
                    // the code reads. Any of them can change what the assembly does or what it is
                    // built from, and none of them says which lines.
                    foreach (ProjectUnderTest project in mutable)
                    {
                        widened.Add(project.ProjectPath);
                    }

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

            // Files that belong to no test project at HEAD may belong to one at the base revision -
            // which is the coverage edge vanishing one layer further out again.
            IReadOnlyList<string> formerTestProjects = await FormerTestProjectsAsync(
                    unattributed, cancellationToken)
                .ConfigureAwait(false);

            if (touchedTestProjects.Count == 0 && formerTestProjects.Count == 0)
            {
                return;
            }

            progress?.Report(new MutationTestProgress(
                MutationTestPhase.SelectingChanges, Subject: Short(baseRevision)));

            using BaseProjectGraph graph = await BaseProjectGraph
                .ExportAsync(repository, baseRevision, configuration, cancellationToken)
                .ConfigureAwait(false);

            List<string> atBase = [.. touchedTestProjects.Select(RepositoryPathOf).OfType<string>()];

            foreach (string candidate in formerTestProjects)
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
        /// Projects that existed at the base revision, own one of these files, and are not test
        /// projects at HEAD.
        /// </summary>
        /// <remarks>
        /// Not "and no longer exist", which is what this asked at first and what review corrected. A
        /// change can leave a test project's file exactly where it is and stop it being a test
        /// project - flip its <c>OutputType</c>, declare it test support, drop the package - and the
        /// suite is just as disabled as if it had been deleted. Asking about existence missed that
        /// entirely: the file was still there, so the project was not a candidate, and if another
        /// suite still reached the same production code the run went green over a suite that had
        /// been switched off.
        /// </remarks>
        private async Task<IReadOnlyList<string>> FormerTestProjectsAsync(
            IReadOnlyList<FileChange> unattributed,
            CancellationToken cancellationToken)
        {
            if (unattributed.Count == 0)
            {
                return [];
            }

            HashSet<string> testProjectsAtHead = new(
                testProjects.Select(test => RepositoryPathOf(test.ProjectPath)).OfType<string>(),
                RepositoryPath.Comparer);

            string[] candidates = [.. (await repository
                    .ListFilesAsync(baseRevision, cancellationToken).ConfigureAwait(false))
                .Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                .Where(path => !testProjectsAtHead.Contains(path))];

            if (candidates.Length == 0)
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

                foreach (string project in candidates)
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

        /// <summary>
        /// Every test project that owns <paramref name="path"/>: by what it compiles, or failing
        /// that by where it sits.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Two rules in a union, because each covers what the other cannot. Evaluated membership is
        /// exact and catches a file the project reaches out of its own folder for - review found
        /// that a test project including <c>../SharedTests/Assertions.cs</c> had that change read as
        /// production code, so deleting an assertion from it produced an empty, passing run.
        /// </para>
        /// <para>
        /// The directory rule catches what evaluation cannot see: a file the change deleted is no
        /// longer in HEAD's item list, and a deleted test file is the case the widening rule was
        /// written for in the first place.
        /// </para>
        /// <para>
        /// What neither covers is a file both deleted and linked from outside every test project's
        /// directory. Recorded rather than pretended away.
        /// </para>
        /// </remarks>
        private async Task<IReadOnlyList<string>> TestProjectsOwningAsync(
            string path,
            CancellationToken cancellationToken)
        {
            List<string> byDirectory = Owning(_testProjectsByDirectory, path);

            if (byDirectory.Count > 0)
            {
                return byDirectory;
            }

            Dictionary<string, HashSet<string>> inputs = await TestProjectInputsAsync(cancellationToken)
                .ConfigureAwait(false);

            string full = Path.GetFullPath(path);

            return [.. inputs.Where(entry => entry.Value.Contains(full)).Select(entry => entry.Key)];
        }

        /// <summary>
        /// What each test project compiles or carries, read once and only when a change needs it.
        /// </summary>
        /// <remarks>
        /// One MSBuild evaluation per test project, on a partial run only, and only once a change
        /// has landed outside every test project's directory. A full run never pays it, and neither
        /// does a partial run whose files all sit where their projects do.
        /// </remarks>
        private async Task<Dictionary<string, HashSet<string>>> TestProjectInputsAsync(
            CancellationToken cancellationToken)
        {
            if (_testProjectInputs is not null)
            {
                return _testProjectInputs;
            }

            var msBuild = new MsBuildQuery(configuration);
            Dictionary<string, HashSet<string>> inputs = new(ProjectPaths.Comparer);

            foreach (TestProject testProject in testProjects.DistinctBy(
                         test => test.ProjectPath, ProjectPaths.Comparer))
            {
                inputs[testProject.ProjectPath] = new HashSet<string>(
                    await msBuild
                        .GetInputFilesAsync(testProject.ProjectPath, cancellationToken: cancellationToken)
                        .ConfigureAwait(false),
                    ProjectPaths.Comparer);
            }

            return _testProjectInputs = inputs;
        }

        private List<ProjectUnderTest> MutableProjectsOwning(string path) =>
            Owning(_mutableProjectsByDirectory, path);

        private IEnumerable<string> TestProjectsBeneath(string directory) =>
            testProjects
                .Where(test => IsUnder(Path.GetDirectoryName(test.ProjectPath), directory))
                .Select(test => test.ProjectPath);

        /// <summary>
        /// The projects of the nearest enclosing directory, or none when the file is under none.
        /// </summary>
        /// <remarks>
        /// The longest match wins, because projects nest: a file under <c>src/Core/Sub</c> belongs to
        /// <c>src/Core/Sub</c> if there is a project there, and to <c>src/Core</c> otherwise. All the
        /// projects at that depth are returned, since a directory may hold more than one.
        /// </remarks>
        private static List<T> Owning<T>(Dictionary<string, List<T>> byDirectory, string path)
        {
            string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
            List<T> found = [];
            int longest = -1;

            foreach ((string candidate, List<T> values) in byDirectory)
            {
                if (candidate.Length > longest && IsUnder(directory, candidate))
                {
                    found = values;
                    longest = candidate.Length;
                }
            }

            return found;
        }

        private static Dictionary<string, List<TValue>> Grouped<TSource, TValue>(
            IEnumerable<TSource> source,
            Func<TSource, string> directory,
            Func<TSource, TValue> value)
        {
            Dictionary<string, List<TValue>> grouped = new(ProjectPaths.Comparer);

            foreach (TSource item in source)
            {
                string key = directory(item);

                if (!grouped.TryGetValue(key, out List<TValue>? values))
                {
                    grouped[key] = values = [];
                }

                values.Add(value(item));
            }

            return grouped;
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
