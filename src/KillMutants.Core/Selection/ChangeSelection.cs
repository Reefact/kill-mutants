using KillMutants.Filtering;
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
/// a change to a test removes is a coverage <em>edge</em>, and the current state cannot be asked about an edge
/// that is no longer there.
/// </para>
/// <para>
/// The relation is read at both states, <c>targets(before) ∪ targets(now)</c>. Reading the current one alone
/// would let the same hole reappear one layer down: remove the <c>ProjectReference</c> from
/// <c>Tests</c> to <c>ProjectA</c> in the change being judged, and the current graph no longer says <c>Tests</c>
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
    /// root <c>Directory.Build.props</c> means all of them - a partial run that is briefly
    /// a full one. That is the honest answer: these files decide what is compiled, against which
    /// package versions, with which constants. Documentation, workflows and everything else outside a
    /// project are ignored, so a docs change selects nothing and finishes at once.
    /// </remarks>
    /// <summary>True when a file decides how projects around it are built.</summary>
    /// <remarks>
    /// Shared by the selection, which widens what sits beneath such a file, and by the base graph,
    /// which refuses when the export left one out: an omitted <c>Directory.Build.props</c> can carry
    /// the project references the graph is about to read, and every <c>.csproj</c> can be present
    /// while the answer is still wrong. Review found that second use missing.
    /// </remarks>
    internal static bool IsSharedBuildFile(string path)
    {
        string name = Path.GetFileName(path);

        return SharedBuildFiles.Contains(name, StringComparer.OrdinalIgnoreCase) ||
               name.EndsWith(".props", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".targets", StringComparison.OrdinalIgnoreCase);
    }

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
    /// Projects the earlier state's tests exercised, that still exist, and that no test project
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
    /// <param name="source">Where the change, and the code before it, are read from.</param>
    /// <param name="searchDirectory">The directory the run was pointed at.</param>
    /// <param name="configuration">The build configuration, for reading the earlier graph.</param>
    /// <param name="discovered">What discovery found now, and what it consumes.</param>
    /// <param name="exclusions">What the run was told to leave alone.</param>
    /// <param name="progress">Told when the earlier state is being read, which is the slow part.</param>
    /// <param name="cancellationToken">Cancels the resolution.</param>
    /// <exception cref="ChangeSelectionException">
    /// The change, or the earlier state's project graph, could not be read - or the change touches
    /// the run's own configuration, which a partial run cannot judge.
    /// </exception>
    public static async Task<ChangeSelection> ResolveAsync(
        IChangeSource source,
        string searchDirectory,
        string configuration,
        DiscoveredProjects discovered,
        PathFilter exclusions,
        IProgress<MutationTestProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(discovered);
        ArgumentNullException.ThrowIfNull(exclusions);

        ChangeSet change = await source.ChangesAsync(cancellationToken).ConfigureAwait(false);

        RefuseIfTheGateItselfChanged(change.Changes, Path.GetFullPath(searchDirectory));

        var scope = new RunScope(
            change.ComparedFrom, change.ComparedTo, change.ComparedToIsExact, change.Changes.Count);

        var resolver = new Resolver(
            source,
            change.ComparedFrom,
            configuration,
            Path.GetFullPath(searchDirectory),
            discovered,
            exclusions,
            progress);

        return await resolver.ResolveAsync(scope, change.Changes, cancellationToken).ConfigureAwait(false);
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
    /// Comparing the two states' settings and keeping whatever the change removed would be the
    /// precise answer. This is the honest one: a partial run cannot judge a change to its own
    /// configuration, so it declines to, and says to run without <c>--since</c>. Declining to answer
    /// is a thing this tool is allowed to do; answering as though the question were unchanged is not.
    /// </para>
    /// </remarks>
    private static void RefuseIfTheGateItselfChanged(
        IReadOnlyList<FileChange> changes,
        string searchDirectory)
    {
        // The file this run actually reads, not any file of that name in the codebase. Review
        // found the difference: a monorepo measured one component at a time had every partial run
        // refused by a change to a sibling component's settings, which decide nothing here.
        string settings = Path.Combine(searchDirectory, "killmutants.json");

        FileChange? configuration = changes.FirstOrDefault(change =>
            ProjectPaths.Comparer.Equals(Path.GetFullPath(change.Path), settings));

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
        IChangeSource source,
        string comparedFrom,
        string configuration,
        string searchDirectory,
        DiscoveredProjects discovered,
        PathFilter exclusions,
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
                // A whole component, said by the source rather than guessed here. Everything after
                // this reads the path's parent directory, which for a subtree is the wrong project
                // entirely, so it is taken conservatively before anything else looks at it.
                //
                // Review found the guess: this asked whether the path was a directory on disk, which
                // is a question about the code as it is now. A component the change *removed* is not
                // there any more, so the answer was "file", the path matched no project, and the run
                // passed over a subtree that had gone.
                if (change.IsWholeComponent)
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

            // Everything in `widened` at this point was widened by a change to something other than
            // a source file - a project file, a resource, a shared build file - because a changed
            // `.cs` file goes to the selected set instead. Those are exactly the changes that can
            // delete a project reference, so the suites reaching them are read at the earlier state
            // as well. Review found the hole: `Tests -> Facade -> Core` becoming `Tests -> Facade`
            // puts only `Facade.csproj` in the diff, no test project is touched, the base graph is
            // never consulted, and `Core` leaves the targets without anyone noticing.
            IReadOnlyList<string> coverageLost = await WidenForTestsAsync(
                    widened,
                    touchedTestProjects,
                    SuitesReaching(widened),
                    unattributed,
                    changes,
                    cancellationToken)
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
        /// The exception for an added file is narrower than it was, in two directions review pushed
        /// it. A new <em>test</em> cannot remove a coverage edge that predates it, which is what
        /// DEC0011 argues; a new fixture, case list or settings file consumed by existing tests can
        /// change what they do, so only an added C# file keeps the exception. And it is an argument
        /// about tests, so it applies to a test project's own files and to nothing else: a support
        /// library the run left out is explicitly not a test project, and its files are helpers every
        /// suite compiles against. What remains is that an added C# file in a suite could hold shared
        /// setup or a module initializer rather than a test - recorded in DEC0011 rather than assumed
        /// away.
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

            // No added-file exception on this branch, and review was right that it never belonged.
            // A project the run left out is not a test project; its files are helpers every suite
            // compiles against, so an added one - a module initializer, an assembly attribute, a new
            // part of an existing partial type - changes what the existing tests do with no test
            // file in the diff at all. The generator path already refused the exception for the same
            // reason.
            if (discovered.LeftOut.TryGetValue(project, out IReadOnlyList<string>? reachedBy))
            {
                foreach (string testProject in reachedBy)
                {
                    touchedTestProjects.Add(testProject);
                }

                return true;
            }

            // A generator: referenced as an analyzer, so it runs at build time and never at run
            // time. It is neither a target nor anything a target links, and review found the
            // consequence - changing a generator's source changes what its consumers compile, while
            // the diff holds no line of their code, so the run selected nothing and passed. The
            // change is attributed to each consumer instead, which gives it whatever role that
            // project has.
            bool attributed = false;

            if (discovered.AnalyzerConsumers.TryGetValue(project, out IReadOnlyList<string>? consumers))
            {
                foreach (string consumer in consumers)
                {
                    attributed |= WidenConsumer(consumer, widened, touchedTestProjects);
                }
            }

            // Additive here too, and review found the early return that was not. Being a generator
            // and being a target are not exclusive: one suite can reference a project normally while
            // another consumes it as an analyzer. Returning as soon as a consumer was reached
            // re-ran the consumer and left the project's own changed compilation unmeasured.
            if (!discovered.TargetPaths.Contains(project))
            {
                return attributed;
            }

            if (!IsCSharp(change.Path))
            {
                widened.Add(project);
            }

            return true;
        }

        /// <summary>
        /// Gives a change to a generator the role its consumer has, whole rather than file by file.
        /// </summary>
        /// <remarks>
        /// A generator's own file is never in a consumer's compilation - the trees it contributes
        /// carry generated paths - so there is no precise selection to make here. What a changed
        /// generator can do is change every one of those trees, which is the whole project.
        /// </remarks>
        private bool WidenConsumer(
            string consumer,
            HashSet<string> widened,
            HashSet<string> touchedTestProjects)
        {
            // No added-file exception on this path, and review was right to separate them. That
            // exception says a new *test* cannot remove an edge that predates it; a generator source
            // the change adds is not a test, and it can change what every existing test compiles to.
            if (discovered.TestProjectPaths.Contains(consumer))
            {
                touchedTestProjects.Add(consumer);

                return true;
            }

            if (discovered.LeftOut.TryGetValue(consumer, out IReadOnlyList<string>? reachedBy))
            {
                foreach (string testProject in reachedBy)
                {
                    touchedTestProjects.Add(testProject);
                }

                return true;
            }

            if (!discovered.TargetPaths.Contains(consumer))
            {
                return false;
            }

            widened.Add(consumer);

            return true;
        }

        /// <summary>
        /// The test projects that exercise any of these production projects now.
        /// </summary>
        /// <remarks>
        /// Read from the targets rather than from the project graph, because a target already pairs
        /// a project under test with the suites measuring it, and that pairing is what the base-side
        /// traversal needs a root for.
        /// </remarks>
        private List<string> SuitesReaching(IReadOnlyCollection<string> projects)
        {
            List<string> suites = [];

            foreach (MutationTestTarget target in discovered.Targets.Where(target =>
                         projects.Contains(target.ProjectUnderTest.ProjectPath, ProjectPaths.Comparer)))
            {
                suites.AddRange(target.TestProjects.Select(test => test.ProjectPath));
            }

            return [.. suites.Distinct(ProjectPaths.Comparer)];
        }

        /// <summary>
        /// Widens to every project the touched test projects exercise, in both states.
        /// </summary>
        /// <param name="widened">The projects taken whole, added to as the base graph is read.</param>
        /// <param name="touchedTestProjects">
        /// Suites a change touched. Each widens what it exercises now, and is read at the earlier state.
        /// </param>
        /// <param name="tracedAtBase">
        /// Suites read at the earlier state only. A change to a target's project file can delete a
        /// reference the suite still has - so the old graph has to be asked - without changing
        /// anything about the rest of what that suite exercises, which is why these do not widen.
        /// </param>
        /// <param name="unattributed">
        /// Changes no project claimed. Only a count is wanted here: a change nothing claimed is a
        /// reason to look at the earlier state at all, rather than return with nothing read.
        /// </param>
        /// <param name="changes">Every change, asked which projects stopped being suites.</param>
        /// <param name="cancellationToken">Cancels the traversal.</param>
        private async Task<IReadOnlyList<string>> WidenForTestsAsync(
            HashSet<string> widened,
            HashSet<string> touchedTestProjects,
            List<string> tracedAtBase,
            List<FileChange> unattributed,
            IReadOnlyList<FileChange> changes,
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

            // Reading the earlier state costs an MSBuild evaluation, so it is not done for
            // nothing - but "nothing" cannot mean "every change was claimed", which is what it meant
            // until review found two ways a claimed change disables a suite. A project file or a
            // shared build file is what decides whether a project still is one, and in the ordinary
            // case such a change also widens or touches something, so this last clause never fires.
            if (touchedTestProjects.Count == 0 &&
                tracedAtBase.Count == 0 &&
                unattributed.Count == 0 &&
                !changes.Any(change => DecidesWhatAProjectIs(change.Path)))
            {
                return [];
            }

            progress?.Report(new MutationTestProgress(
                MutationTestPhase.SelectingChanges, Subject: Short(comparedFrom)));

            ICodeSnapshot before = await source
                .OpenCodeBeforeAsync(cancellationToken)
                .ConfigureAwait(false);

            using BaseProjectGraph graph = BaseProjectGraph.Open(before, comparedFrom, configuration);

            // Files that belong to no test project now may belong to one in the earlier state -
            // the coverage edge vanishing one layer further out again. Asked of the graph, which
            // already knows every project that state held, rather than of the source a second time.
            IReadOnlyList<string> formerTestProjects = FormerTestProjects(changes, graph);

            // Two kinds of root, and review found that treating them alike undid the point of
            // separating them in the first place. A suite the change touched, or one that stopped
            // being a suite, widens what it reached: the change may have altered what it kills. A
            // suite read only because a project it reaches had its project file changed widens
            // nothing - the change says nothing about the rest of what that suite exercises, and
            // adding it would fail a change confined to A on an old survivor in B.
            List<string> widening = [.. touchedTestProjects.Select(RelativePathOf).OfType<string>()];

            foreach (string candidate in formerTestProjects)
            {
                if (await graph.IsTestProjectAsync(candidate, cancellationToken).ConfigureAwait(false))
                {
                    widening.Add(candidate);
                }
            }

            HashSet<string> wideningRoots = new(widening, RelativePath.Comparer);
            SortedSet<string> coverageLost = new(StringComparer.Ordinal);

            IEnumerable<string> roots = widening
                .Concat(tracedAtBase.Select(RelativePathOf).OfType<string>())
                .Distinct(RelativePath.Comparer);

            foreach (string testProject in roots)
            {
                bool widens = wideningRoots.Contains(testProject);

                foreach (string reached in await graph
                             .ProductionProjectsReachedFromAsync(testProject, cancellationToken)
                             .ConfigureAwait(false))
                {
                    if (CurrentTargetAt(reached) is { } target)
                    {
                        if (widens)
                        {
                            widened.Add(target);
                        }

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
        /// True when a project the earlier state's tests reached is still there and no longer
        /// measured by anything.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Five questions, and each excludes a different innocent case. Gone from disk: the change
        /// deleted it, and there is nothing left to cover. A target now: still measured. A test
        /// project now: it became the yardstick rather than the subject. Left out on purpose:
        /// excluded, or declaring itself test support, which is the user saying not to measure it.
        /// What remains is a project that still exists, that nothing was told to ignore, and that no
        /// suite reaches any more.
        /// </para>
        /// <para>
        /// The fifth is the run's own scope, and review found it missing. A codebase is not
        /// always measured whole: point the run at one directory and a suite inside it may reference
        /// a project outside, which the base graph returns and discovery never saw. Absent from the
        /// targets and from what was left out on purpose, it read as newly uncovered - and since
        /// nothing about it changes between runs, every partial run in such a codebase failed.
        /// Out of scope is not uncovered; it was never this run's to measure.
        /// </para>
        /// </remarks>
        private bool StoppedBeingCovered(string relativePath)
        {
            string absolute = RelativePath.In(source.Root, relativePath);

            return File.Exists(absolute) &&
                   InScope(relativePath) &&
                   !ExcludedByConfiguration(absolute);
        }

        /// <summary>
        /// True when the run was told to leave this project alone, rather than told so by the diff.
        /// </summary>
        /// <remarks>
        /// The distinction review found missing, and it is the same shape as a changed
        /// <c>killmutants.json</c>: an opt-out the change being judged introduced cannot be taken at
        /// face value. Excluding a project comes from the run's configuration, which a change cannot
        /// alter without the run refusing to judge it at all. Declaring a project test support, or
        /// turning it into a test project, comes from a project file the diff may have just written
        /// - and both take it out of the targets exactly as losing its last test would.
        /// <para>
        /// Asking the earlier state is unnecessary because it has already answered: that
        /// traversal returns production projects only, walking through test support and stopping at
        /// test projects, so anything it reached was an ordinary production project then. If it is
        /// opted out now, this diff is what opted it out.
        /// </para>
        /// <para>
        /// The configuration is asked directly rather than through what discovery left out, and
        /// review found why it has to be. What was left out is a list a traversal fills as it walks;
        /// a change that removes the last reference to an excluded project leaves that walk nowhere
        /// to reach it, so the list is empty of it and the earlier state's graph brings it back as
        /// newly uncovered. The gate then failed over a project the user had taken out of mutation
        /// testing - the one false positive it had. The patterns are the instruction, and they say
        /// the same thing whether or not anything still points at the project.
        /// </para>
        /// </remarks>
        private bool ExcludedByConfiguration(string absolute) => exclusions.Excludes(absolute);

        /// <summary>
        /// Projects that existed in the earlier state, own one of these files, and are not test
        /// projects now.
        /// </summary>
        /// <remarks>
        /// Not "and no longer exist", which is what this asked at first and what review corrected. A
        /// change can leave a test project's file exactly where it is and stop it being a test
        /// project - flip its <c>OutputType</c>, declare it test support, drop the package - and the
        /// suite is just as disabled as if it had been deleted.
        /// <para>
        /// Asked of every change rather than of the ones nothing else claimed, which review found
        /// twice. Attribution says what a change <em>selects</em>; this asks what it <em>disabled</em>,
        /// and one file does both: a project file that stops its suite being a suite can hand the
        /// project another role in the same edit, and a shared build file is claimed for the suites
        /// beneath it before it is ever read here.
        /// </para>
        /// </remarks>
        private IReadOnlyList<string> FormerTestProjects(
            IReadOnlyList<FileChange> changes,
            BaseProjectGraph graph)
        {
            if (changes.Count == 0)
            {
                return [];
            }

            HashSet<string> testProjectsNow = new(
                discovered.TestProjectPaths.Select(RelativePathOf).OfType<string>(),
                RelativePath.Comparer);

            // Inside the run's scope, like the coverage-loss check above. Review found the same
            // omission here: a changed project file in a sibling directory was read as a former
            // test project purely because discovery, pointed elsewhere, had never seen it - and
            // if it reached an in-scope target in the earlier state, an unrelated change next
            // door widened that target and could fail the gate on its existing mutants.
            // Left alone on purpose, and review found this the same false positive as the one in
            // StoppedBeingCovered, one level up. Being a suite *now* is read from what discovery
            // found, and discovery never evaluates an excluded project at all - so an excluded suite
            // is absent from that set for a reason that has nothing to do with the change, and would
            // always answer "stopped being a suite". The patterns are the instruction here too.
            string[] candidates = [.. graph.ProjectFiles
                .Where(InScope)
                .Where(path => !testProjectsNow.Contains(path))
                .Where(path => !ExcludedByConfiguration(RelativePath.In(source.Root, path)))];

            if (candidates.Length == 0)
            {
                return [];
            }

            HashSet<string> owning = new(RelativePath.Comparer);

            foreach (FileChange change in changes)
            {
                if (RelativePathOf(change.Path) is not { } relative)
                {
                    continue;
                }

                // Two kinds of change speak for what is beneath them rather than for the project
                // whose folder holds them: a shared build file, which decides what those projects
                // evaluate to, and a whole component, which is the source saying it can name no
                // more than the subtree. Review found the second missing here.
                string? from =
                    change.IsWholeComponent ? relative
                    : IsSharedBuildFile(relative) ? RelativePath.DirectoryOf(relative)
                    : null;

                foreach (string project in candidates)
                {
                    string directory = RelativePath.DirectoryOf(project);

                    if (RelativePath.IsUnder(relative, directory) ||
                        (from is not null && RelativePath.IsUnder(directory, from)))
                    {
                        owning.Add(project);
                    }
                }
            }

            return [.. owning];
        }

        /// <summary>True when a relative path is under the directory the run was pointed at.</summary>
        private bool InScope(string relativePath) =>
            IsUnder(
                Path.GetDirectoryName(RelativePath.In(source.Root, relativePath)),
                searchDirectory);

        private string? CurrentTargetAt(string relativePath)
        {
            string absolute = RelativePath.In(source.Root, relativePath);

            return discovered.TargetPaths.FirstOrDefault(
                path => ProjectPaths.Comparer.Equals(path, absolute));
        }

        private string? RelativePathOf(string path) => RelativePath.Of(source.Root, path);

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

        /// <summary>Gives every project beneath a directory the role it holds, wherever from.</summary>
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

            // A file inside the component, compiled or carried by a project outside it. Review
            // found this the last way a component reaches out that the loops here did not know:
            // every one of them is keyed on a *project* sitting under the path, and a
            // `<Compile Include="../vendor/shared/**/*.cs" />` puts no project there at all. What
            // changed is a file, and the projects that read it are exactly what this index answers.
            foreach ((string file, List<string> consumers) in _consumersByFile)
            {
                if (!IsUnder(Path.GetDirectoryName(file), directory))
                {
                    continue;
                }

                foreach (string consumer in consumers)
                {
                    WidenConsumer(consumer, widened, touchedTestProjects);
                }
            }

            // Two more roles, and review found them missing. The loops above know what sits under
            // the path; these know what reaches into it from outside. A library the run leaves out,
            // or a generator, is neither a target beneath nor a suite beneath - so a component that
            // moved changed what its consumers compile, and nothing was selected. The role travels
            // here exactly as it would for a changed file inside one of those projects, because a
            // source that can only name the component has said the same thing about all of them.
            foreach ((string project, IReadOnlyList<string> reachedBy) in discovered.LeftOut)
            {
                if (IsUnder(Path.GetDirectoryName(project), directory))
                {
                    foreach (string testProject in reachedBy)
                    {
                        touchedTestProjects.Add(testProject);
                    }
                }
            }

            foreach ((string generator, IReadOnlyList<string> consumers) in discovered.AnalyzerConsumers)
            {
                if (IsUnder(Path.GetDirectoryName(generator), directory))
                {
                    foreach (string consumer in consumers)
                    {
                        WidenConsumer(consumer, widened, touchedTestProjects);
                    }
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

        /// <summary>True when a change can alter what a project <em>is</em>, not only what it holds.</summary>
        private static bool DecidesWhatAProjectIs(string path) =>
            IsSharedBuildFile(path) ||
            path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);

        private static string Short(string label) => label.Length > 12 ? label[..12] : label;
    }
}
