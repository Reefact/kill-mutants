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
/// <para>
/// A changed file is attributed to the projects that <em>consume</em> it - MSBuild's evaluated items,
/// in union with the directory it sits in - and then plays every role those projects give it. Review
/// found the alternative the hard way, four times over: a rule that asks "which one project owns
/// this file" has to guess, and every guess it made was wrong for some legal layout.
/// </para>
/// </remarks>
internal sealed class ChangeSelection
{
    /// <summary>
    /// Files outside a project directory that a build reads, and that change what the code does.
    /// </summary>
    /// <remarks>
    /// A change to one of these widens every project beneath it, which in the usual case of a
    /// repository-root <c>Directory.Build.props</c> means all of them - a partial run that is briefly
    /// a full one. That is the honest answer: these files decide what is compiled, against which
    /// package versions, with which constants. Documentation, workflows and everything else outside a
    /// project are ignored, so a docs change selects nothing and finishes at once.
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

    private ChangeSelection(
        RunScope scope,
        HashSet<string> widened,
        MutantSelection changedFiles,
        IReadOnlyList<string> coverageLost)
    {
        Scope = scope;
        _widened = widened;
        _changedFiles = changedFiles;
        CoverageLost = coverageLost;
    }

    /// <summary>What the report says about the population this run inspected.</summary>
    public RunScope Scope { get; }

    /// <summary>
    /// Projects the base revision's tests exercised, that still exist, and that no test project
    /// reaches any more.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one thing the widening cannot express, and review found it: remove the last
    /// <c>ProjectReference</c> from a suite to a project and that project leaves the run entirely -
    /// there is no target for it, so no compilation, so no mutants to select. The selection stays
    /// empty and the verdict passes, over a component whose coverage the change has just deleted.
    /// </para>
    /// <para>
    /// It is reported rather than mutated, because there is nothing to mutate it against: no test
    /// project reaches it, so no suite could judge a mutant in it. What the run can say is that it
    /// used to be covered and is not any more, and that a partial run which cannot ask about a
    /// component has not passed.
    /// </para>
    /// <para>
    /// A project the run was told to leave alone - excluded, or declaring itself test support - is
    /// not this. Those are deliberate, and <see cref="ProjectDiscovery.ProjectsLeftOut"/> is how the
    /// two are told apart.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> CoverageLost { get; }

    /// <summary>
    /// True when nothing in the change can produce a mutant, so there is nothing to build or run.
    /// </summary>
    /// <remarks>
    /// A conservative check, made before anything expensive: no project was widened and no C# file
    /// changed. A change that touches C# files belonging to no compilation still goes the long way
    /// round and finds no mutants, which is slower and never wrong.
    /// </remarks>
    public bool SelectsNothing =>
        _widened.Count == 0 && _changedFiles.IsEmpty && CoverageLost.Count == 0;

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
    /// <param name="discovered">What discovery found at HEAD, and what it consumes.</param>
    /// <param name="progress">Told when the base revision is being read, which is the slow part.</param>
    /// <param name="cancellationToken">Cancels the resolution.</param>
    /// <exception cref="ChangeSelectionException">
    /// The change, or the base revision's project graph, could not be read - or the change touches
    /// the run's own configuration, which a partial run cannot judge.
    /// </exception>
    public static async Task<ChangeSelection> ResolveAsync(
        string since,
        string searchDirectory,
        string configuration,
        DiscoveredProjects discovered,
        IProgress<MutationTestProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(since);
        ArgumentNullException.ThrowIfNull(discovered);

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

        RefuseIfTheGateItselfChanged(changes);

        var scope = new RunScope(
            baseRevision,
            head,
            await repository.HasUncommittedChangesAsync(cancellationToken).ConfigureAwait(false),
            changes.Count);

        var resolver = new Resolver(repository, baseRevision, configuration, discovered, progress);

        return await resolver.ResolveAsync(scope, changes, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Refuses a partial run whose change edits the settings that decide what a run measures.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Review found the sharp version of this: <c>exclude</c> in <c>killmutants.json</c> takes effect
    /// in discovery, before any selection exists, so a change that adds one removes a project from
    /// the targets and no widening afterwards can reach it. A pull request could switch mutation
    /// testing off for a component by editing the file that configures the gate, and the gate would
    /// report a clean partial run over its own disarming.
    /// </para>
    /// <para>
    /// Comparing the two revisions' settings and keeping whatever the change removed would be the
    /// precise answer. This is the honest one: a partial run cannot judge a change to its own
    /// configuration, so it declines to, and says to run without <c>--since</c>. Declining to answer
    /// is a thing this tool is allowed to do; answering as though the question were unchanged is not.
    /// </para>
    /// </remarks>
    private static void RefuseIfTheGateItselfChanged(IReadOnlyList<FileChange> changes)
    {
        FileChange? configuration = changes.FirstOrDefault(change =>
            Path.GetFileName(change.Path).Equals("killmutants.json", StringComparison.OrdinalIgnoreCase));

        if (configuration is not null)
        {
            throw new ChangeSelectionException(
                $"'{Path.GetFileName(configuration.Path)}' changed in this diff, and it decides what a " +
                "run measures - which projects are excluded, which mutators run, whether coverage is " +
                "measured. A partial run cannot judge a change to its own configuration, because the " +
                "new settings would already have narrowed what it looked at. Run without --since.");
        }
    }

    /// <summary>
    /// Everything the resolution needs to keep in hand while it classifies one change's files.
    /// </summary>
    private sealed class Resolver(
        GitRepository repository,
        string baseRevision,
        string configuration,
        DiscoveredProjects discovered,
        IProgress<MutationTestProgress>? progress)
    {
        /// <summary>Every project discovery read, by the directory it sits in.</summary>
        /// <remarks>
        /// A list per directory, not one project: two projects in one folder is unusual and legal,
        /// and review found that assuming otherwise threw a duplicate-key exception before a single
        /// change had been classified.
        /// </remarks>
        private readonly Dictionary<string, List<string>> _projectsByDirectory =
            Grouped(discovered.AllProjects, project => Path.GetDirectoryName(project)!);

        /// <summary>Which projects consume each file, inverted from what discovery evaluated.</summary>
        /// <remarks>
        /// The authoritative half of attribution. A project can compile or carry a file from
        /// anywhere - a <c>Compile</c> with a <c>Link</c>, an <c>EmbeddedResource</c> reached with
        /// <c>..</c>, a glob leaving the folder - and the directory a file sits in says nothing about
        /// any of that.
        /// </remarks>
        private readonly Dictionary<string, List<string>> _consumersByFile = Inverted(discovered.Inputs);

        public async Task<ChangeSelection> ResolveAsync(
            RunScope scope,
            IReadOnlyList<FileChange> changes,
            CancellationToken cancellationToken)
        {
            HashSet<string> widened = new(ProjectPaths.Comparer);
            HashSet<string> changedFiles = new(ProjectPaths.Comparer);
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
                    WidenBeneath(widened, touchedTestProjects, change.Path);

                    continue;
                }

                // Additive from here on. A file plays every role its consumers give it, because it
                // can have more than one: a source compiled by a suite and by a library, a resource
                // one project owns and another links. Review found each of those in turn behind a
                // rule that stopped at the first match.
                bool attributed = false;

                foreach (string project in ProjectsConsuming(change.Path))
                {
                    attributed |= Attribute(change, project, widened, touchedTestProjects);
                }

                if (IsCSharp(change.Path))
                {
                    // Harmless when the file is a test's own: a test project's compilation is never
                    // mutated, so its files simply never match one. What it stops is a production
                    // file being dropped because a suite happened to own the directory above it.
                    changedFiles.Add(change.Path);
                    attributed = true;
                }

                // Checked whatever else claimed the file, and not instead: a Directory.Build.props
                // beside a project affects that project and every one beneath it, and attributing it
                // to its neighbour alone left the rest of the tree unselected.
                if (IsSharedBuildFile(change.Path))
                {
                    WidenBeneath(widened, touchedTestProjects, Path.GetDirectoryName(change.Path)!);
                    attributed = true;
                }

                if (!attributed)
                {
                    unattributed.Add(change);
                }
            }

            IReadOnlyList<string> coverageLost = await WidenForTestsAsync(
                    widened, touchedTestProjects, unattributed, cancellationToken)
                .ConfigureAwait(false);

            return new ChangeSelection(
                scope, widened, MutantSelection.Of(changedFiles), coverageLost);
        }

        /// <summary>
        /// Gives a change the role one consuming project has for it, and says whether it had one.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Three kinds of consumer and three answers. A test project: the change is test-side, and
        /// widens what that suite exercises. A declared test-support library: also test-side, through
        /// the suites that reach it - review found that a changed helper was added to the selected
        /// files and then matched no compilation, since support is not a target, so a helper change
        /// that stops tests reaching production behaviour selected nothing at all. A target: a source
        /// file is selected precisely by the caller, anything else widens the project, since a
        /// resource or a project file can change what the assembly does without saying which lines.
        /// </para>
        /// <para>
        /// The exception for an added file is narrower than it was. A new <em>test</em> cannot remove
        /// a coverage edge that predates it, which is what DEC0011 argues; a new fixture, case list or
        /// settings file consumed by existing tests can change what they do, and review was right
        /// that the rationale does not stretch to cover it. So only an added C# file keeps the
        /// exception. What remains is that an added C# file could hold shared setup or a module
        /// initializer rather than a test - recorded in DEC0011 rather than assumed away.
        /// </para>
        /// </remarks>
        private bool Attribute(
            FileChange change,
            string project,
            HashSet<string> widened,
            HashSet<string> touchedTestProjects)
        {
            bool addedSource = change.Kind == ChangeKind.Added && IsCSharp(change.Path);

            if (discovered.TestProjectPaths.Contains(project))
            {
                if (!addedSource)
                {
                    touchedTestProjects.Add(project);
                }

                return true;
            }

            if (discovered.LeftOut.TryGetValue(project, out IReadOnlyList<string>? reachedBy))
            {
                if (!addedSource)
                {
                    foreach (string testProject in reachedBy)
                    {
                        touchedTestProjects.Add(testProject);
                    }
                }

                return true;
            }

            if (!discovered.TargetPaths.Contains(project))
            {
                return false;
            }

            if (!IsCSharp(change.Path))
            {
                widened.Add(project);
            }

            return true;
        }

        /// <summary>
        /// Widens to every project the touched test projects exercise, at both revisions.
        /// </summary>
        private async Task<IReadOnlyList<string>> WidenForTestsAsync(
            HashSet<string> widened,
            HashSet<string> touchedTestProjects,
            IReadOnlyList<FileChange> unattributed,
            CancellationToken cancellationToken)
        {
            foreach (string testProject in touchedTestProjects)
            {
                foreach (MutationTestTarget target in discovered.Targets.Where(target =>
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
                return [];
            }

            progress?.Report(new MutationTestProgress(
                MutationTestPhase.SelectingChanges, Subject: Short(baseRevision)));

            IReadOnlyList<string> filesAtBase = await repository
                .ListFilesAsync(baseRevision, cancellationToken)
                .ConfigureAwait(false);

            using BaseProjectGraph graph = await BaseProjectGraph
                .ExportAsync(repository, baseRevision, configuration, filesAtBase, cancellationToken)
                .ConfigureAwait(false);

            List<string> atBase = [.. touchedTestProjects.Select(RepositoryPathOf).OfType<string>()];

            foreach (string candidate in formerTestProjects)
            {
                if (await graph.IsTestProjectAsync(candidate, cancellationToken).ConfigureAwait(false))
                {
                    atBase.Add(candidate);
                }
            }

            SortedSet<string> coverageLost = new(StringComparer.Ordinal);

            foreach (string testProject in atBase.Distinct(RepositoryPath.Comparer))
            {
                foreach (string reached in await graph
                             .ProductionProjectsReachedFromAsync(testProject, cancellationToken)
                             .ConfigureAwait(false))
                {
                    if (HeadTargetAt(reached) is { } target)
                    {
                        widened.Add(target);

                        continue;
                    }

                    if (StoppedBeingCovered(reached))
                    {
                        coverageLost.Add(reached);
                    }
                }
            }

            return [.. coverageLost];
        }

        /// <summary>
        /// True when a project the base revision's tests reached is still there and no longer
        /// measured by anything.
        /// </summary>
        /// <remarks>
        /// Four questions, and each excludes a different innocent case. Gone from disk: the change
        /// deleted it, and there is nothing left to cover. A target at HEAD: still measured. A test
        /// project at HEAD: it became the yardstick rather than the subject. Left out on purpose:
        /// excluded, or declaring itself test support, which is the user saying not to measure it.
        /// What remains is a project that still exists, that nothing was told to ignore, and that no
        /// suite reaches any more.
        /// </remarks>
        private bool StoppedBeingCovered(string repositoryPath)
        {
            string absolute = RepositoryPath.In(repository.Root, repositoryPath);

            return File.Exists(absolute) &&
                   !discovered.LeftOut.ContainsKey(absolute) &&
                   !discovered.TestProjectPaths.Contains(absolute);
        }

        /// <summary>
        /// Projects that existed at the base revision, own one of these files, and are not test
        /// projects at HEAD.
        /// </summary>
        /// <remarks>
        /// Not "and no longer exist", which is what this asked at first and what review corrected. A
        /// change can leave a test project's file exactly where it is and stop it being a test
        /// project - flip its <c>OutputType</c>, declare it test support, drop the package - and the
        /// suite is just as disabled as if it had been deleted.
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
                discovered.TestProjectPaths.Select(RepositoryPathOf).OfType<string>(),
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

            return discovered.TargetPaths.FirstOrDefault(
                path => ProjectPaths.Comparer.Equals(path, absolute));
        }

        private string? RepositoryPathOf(string path) => RepositoryPath.Of(repository.Root, path);

        /// <summary>
        /// Every project that consumes <paramref name="path"/>: by what it evaluates, and by where
        /// it sits.
        /// </summary>
        /// <remarks>
        /// A union, and this time in the code as well as in the comment. Evaluated membership is
        /// exact and sees a file a project reaches out of its own folder for; the directory sees a
        /// file the change <em>deleted</em>, which no evaluation lists any more. Neither covers the
        /// other's blind spot, and taking the first that answered - which is what this did until
        /// review said so - misses every file that has two consumers.
        /// </remarks>
        private IEnumerable<string> ProjectsConsuming(string path)
        {
            string full = Path.GetFullPath(path);

            IEnumerable<string> byEvaluation = _consumersByFile.TryGetValue(full, out List<string>? consumers)
                ? consumers
                : [];

            return byEvaluation
                .Concat(Owning(_projectsByDirectory, path))
                .Distinct(ProjectPaths.Comparer);
        }

        /// <summary>
        /// The projects of the nearest enclosing directory, or none when the file is under none.
        /// </summary>
        /// <remarks>
        /// The longest match wins, because projects nest: a file under <c>src/Core/Sub</c> belongs to
        /// <c>src/Core/Sub</c> if there is a project there, and to <c>src/Core</c> otherwise. All the
        /// projects at that depth are returned, since a directory may hold more than one.
        /// </remarks>
        private static List<string> Owning(Dictionary<string, List<string>> byDirectory, string path)
        {
            string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
            List<string> found = [];
            int longest = -1;

            foreach ((string candidate, List<string> values) in byDirectory)
            {
                if (candidate.Length > longest && IsUnder(directory, candidate))
                {
                    found = values;
                    longest = candidate.Length;
                }
            }

            return found;
        }

        private static Dictionary<string, List<string>> Grouped(
            IEnumerable<string> source,
            Func<string, string> key)
        {
            Dictionary<string, List<string>> grouped = new(ProjectPaths.Comparer);

            foreach (string item in source)
            {
                string directory = key(item);

                if (!grouped.TryGetValue(directory, out List<string>? values))
                {
                    grouped[directory] = values = [];
                }

                values.Add(item);
            }

            return grouped;
        }

        private static Dictionary<string, List<string>> Inverted(
            IReadOnlyDictionary<string, IReadOnlyList<string>> inputsByProject)
        {
            Dictionary<string, List<string>> consumers = new(ProjectPaths.Comparer);

            foreach ((string project, IReadOnlyList<string> files) in inputsByProject)
            {
                foreach (string file in files)
                {
                    if (!consumers.TryGetValue(file, out List<string>? projects))
                    {
                        consumers[file] = projects = [];
                    }

                    projects.Add(project);
                }
            }

            return consumers;
        }

        /// <summary>Widens every project beneath a directory, and marks its suites touched.</summary>
        private void WidenBeneath(
            HashSet<string> widened,
            HashSet<string> touchedTestProjects,
            string directory)
        {
            foreach (MutationTestTarget target in discovered.Targets)
            {
                if (IsUnder(target.ProjectUnderTest.ProjectDirectory, directory))
                {
                    widened.Add(target.ProjectUnderTest.ProjectPath);
                }
            }

            // The suites too, and not only the targets under the file. A tests/Directory.Build.props
            // sits above every suite and beneath no production project, so widening what was
            // physically under it widened nothing at all in the ordinary src/ and tests/ layout.
            foreach (string testProject in discovered.TestProjectPaths)
            {
                if (IsUnder(Path.GetDirectoryName(testProject), directory))
                {
                    touchedTestProjects.Add(testProject);
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
