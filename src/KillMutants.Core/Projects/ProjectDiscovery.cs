using System.Globalization;
using System.Text;
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

    private readonly Dictionary<string, List<string>> _leftOut = new(ProjectPaths.Comparer);
    private readonly Dictionary<string, IReadOnlyList<string>> _inputs = new(ProjectPaths.Comparer);
    private readonly Dictionary<string, List<string>> _analyzerConsumers = new(ProjectPaths.Comparer);
    private readonly MsBuildQuery _msBuild;
    /// <summary>The refusal a run with nothing to mutate ends with, wherever it is raised.</summary>
    internal const string NoTargets =
        "The test projects reference no other project, so there is nothing to mutate.";

    private readonly bool _deferNoTargets;

    private readonly string _configuration;
    private readonly string? _targetFramework;
    private readonly PathFilter _exclusions;

    // The framework each multi-targeted test project is built for, by project path. A build of such a
    // project without one builds them all, and the output the run launches is one of them.
    private readonly Dictionary<string, string> _builtFor = new(ProjectPaths.Comparer);
    private readonly IProgress<MutationTestProgress>? _progress;
    private readonly List<SetAside> _setAside = [];

    /// <param name="configuration">The build configuration to analyse and run.</param>
    /// <param name="exclusions">Paths to leave alone.</param>
    /// <param name="progress">Told where discovery has got to.</param>
    /// <param name="readInputFiles">
    /// Also read what each project consumes, for a run that has to attribute a changed file to the
    /// projects that build it. Off for a full run, which has no use for the answer and would pay for
    /// it in the size of every MSBuild reply.
    /// </param>
    /// <param name="deferNoTargets">
    /// Answer with no targets instead of refusing when the test projects reach nothing. A partial
    /// run has to be able to ask why, and only the caller can tell the two answers apart.
    /// </param>
    /// <param name="targetFramework">
    /// The one framework the test projects run on, or null when the developer named none. See DEC0021.
    /// </param>
    public ProjectDiscovery(
        string configuration,
        PathFilter? exclusions = null,
        IProgress<MutationTestProgress>? progress = null,
        bool readInputFiles = false,
        bool deferNoTargets = false,
        string? targetFramework = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration);

        _configuration = configuration;
        _targetFramework = string.IsNullOrWhiteSpace(targetFramework) ? null : targetFramework.Trim();
        _exclusions = exclusions ?? PathFilter.None;
        _progress = progress;
        _deferNoTargets = deferNoTargets;
        _msBuild = new MsBuildQuery(configuration, readInputFiles);
    }

    /// <summary>
    /// Every test project the last discovery recognised, whether or not it exercises anything.
    /// </summary>
    /// <remarks>
    /// A test project that reaches no mutable project appears in no <see cref="MutationTestTarget"/>,
    /// because there is nothing to pair it with. That is right for a run and wrong for a partial one:
    /// a change can be what emptied it - remove a test project's last project reference and it
    /// exercises nothing now - and the selection has to recognise its files as test-side in order
    /// to ask the earlier state what it used to cover. Reading the test projects back off the targets
    /// missed exactly that case, and an end-to-end test found it.
    /// </remarks>
    public IReadOnlyList<TestProject> TestProjects { get; private set; } = [];

    /// <summary>
    /// Projects a test project reaches that this run deliberately does not mutate.
    /// </summary>
    /// <remarks>
    /// Excluded by the user, or declaring themselves test support. Each is kept with the suites that
    /// reach it, and that is what the property is for: a changed file in a project nothing mutates
    /// still travels to those suites, so the production code they exercise is widened.
    /// <para>
    /// It used to answer a second question - whether a project had stopped being covered or had
    /// simply been left alone - and no longer does. That one is put to the run's exclusion patterns
    /// directly, because what a traversal still reaches is not the same thing as what the user asked
    /// to be left alone: a change removing the last reference to an excluded project empties this
    /// list of it while the instruction stands.
    /// </para>
    /// </remarks>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ProjectsLeftOut =>
        _leftOut.ToDictionary(
            entry => entry.Key, entry => (IReadOnlyList<string>)entry.Value, ProjectPaths.Comparer);

    /// <summary>Everything the last discovery learned, for a caller that needs more than targets.</summary>
    public DiscoveredProjects Everything(IReadOnlyList<MutationTestTarget> targets) =>
        new(
            targets,
            new HashSet<string>(
                TestProjects.Select(test => test.ProjectPath), ProjectPaths.Comparer),
            ProjectsLeftOut,
            InputsByProject,
            AnalyzerConsumers);

    /// <summary>
    /// What each project discovery read consumes, by project path, empty unless it was asked.
    /// </summary>
    /// <remarks>
    /// Every project, not only the targets: a partial run has to attribute a changed file to
    /// whatever builds it, and that includes a test project and a declared support library as much
    /// as a project under test.
    /// </remarks>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> InputsByProject => _inputs;

    /// <summary>Which projects consume each generator project, by the generator's path.</summary>
    /// <remarks>
    /// A project referenced as an analyzer is invisible to everything else here on purpose: it does
    /// not run at run time, so it is neither a target nor something a target links. But it decides
    /// what its consumers compile, so a change to its source changes the assembly under test without
    /// putting a line of that assembly's own code in the diff - review found a partial run passing
    /// clean over exactly that. This is the relation that lets the change reach them.
    /// </remarks>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> AnalyzerConsumers =>
        _analyzerConsumers.ToDictionary(
            entry => entry.Key, entry => (IReadOnlyList<string>)entry.Value, ProjectPaths.Comparer);

    /// <summary>
    /// The projects discovery could not read and the test projects that would not build, each with
    /// its reason, in the order they were met (DEC0022).
    /// </summary>
    public IReadOnlyList<SetAside> SetAside => _setAside;

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
            throw new ProjectAnalysisException(WithWhatWasSetAside(
                $"No xUnit test project was found under '{searchDirectory}'. " +
                "KillMutants supports xUnit 4 - the xunit.v3 package family at version 4." +
                WhatWasNotRestored(projects)));
        }

        RequireTheFrameworkTheyRunOn(testProjects);

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

        // Filled only when a test project actually reaches an excluded project, so a codebase
        // that excludes directories nothing references pays nothing for it.
        Dictionary<string, ProjectFacts?> beyondExclusions = new(ProjectPaths.Comparer);

        HashSet<string> restored = new(ProjectPaths.Comparer);

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

                // The framework the suite loads this project for, which is its own only when the
                // project happens to be built for it. A suite on net10.0 referencing a
                // netstandard2.0 library loads the netstandard2.0 build, and asking the library
                // about net10.0 stopped the run on a target it never had. A library that does
                // declare the suite's framework needs no negotiation - an exact match always wins.
                string framework = testProject.TargetFramework;

                if (byPath.TryGetValue(mutablePath, out ProjectFacts? library) &&
                    !ReferencedFramework.Declares(library, framework))
                {
                    // Only a library with a choice needs NuGet's verdict, which is in the suite's
                    // assets file. That file is as old as the last restore: a library edited since
                    // names frameworks the file has never heard of, and trusting it finds no match
                    // and falls back to the very framework this block exists to avoid. So the suite
                    // is restored, for the configuration the run uses, whether or not the file is
                    // there - once per suite, and a no-op when nothing changed.
                    if (library.TargetsSeveralFrameworks && restored.Add(testProject.ProjectPath))
                    {
                        await RestoreAsync(testProject, cancellationToken).ConfigureAwait(false);
                    }

                    framework = ReferencedFramework.For(testProject, library) ?? framework;
                }

                frameworksByProject[mutablePath].Add(framework);
                tests.Add(runnable);
            }
        }

        // Refused for a full run, where it can only be a misconfiguration, and left to the caller
        // for a partial one - review found the difference. A change that removes the last project
        // reference in a small repository empties the targets, and refusing here ends the run
        // before the earlier state can say that a production project has just lost the only suite
        // that covered it. Which of the two it is cannot be decided from the current state alone,
        // and that is the one thing this method has.
        if (testsByProject.Count == 0 && !_deferNoTargets)
        {
            throw new ProjectAnalysisException(WithWhatWasSetAside(NoTargets));
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

            // And recorded, replacing what the framework-less evaluation said this project consumes.
            // Measured: an item conditioned on '$(TargetFramework)' is absent from the outer build of
            // a multi-targeted project, where that property is empty - asked for AdditionalFiles, the
            // outer evaluation answers with the unconditioned file alone. A partial run has to
            // attribute a changed file from the same evaluation the mutated compilation comes from,
            // which is this one.
            Record(facts);

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
                // Reached, and left alone on purpose. Recorded with the suite that reached it, so a
                // partial run can tell this apart from a project whose last test reference a change
                // has just removed - and can widen through it, since a change to a support library
                // is a change to what its suites can see.
                if (!_leftOut.TryGetValue(path, out List<string>? reachedBy))
                {
                    _leftOut[path] = reachedBy = [];
                }

                reachedBy.Add(testProject.ProjectPath);

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
    /// <para>
    /// No framework is named: only the references and the "is this a test project" answer are
    /// needed, and neither depends on which framework the project is evaluated for.
    /// </para>
    /// <para>
    /// Its inputs are recorded like any other project's. Review found them missing here: a project
    /// reached but left out still consumes files, and one it links in from outside its own folder is
    /// attributed by nothing else. A change to such a file then marked no suite as touched and
    /// widened nothing, which is the silent pass the widening exists to prevent.
    /// </para>
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

        if (facts is not null)
        {
            Record(facts);
        }

        cache[path] = facts;

        return facts;
    }

    /// <summary>
    /// Indexes what a project consumes: its own inputs, and the generators it consumes.
    /// </summary>
    /// <remarks>
    /// One method rather than two copies, because review found the second copy missing twice - first
    /// the inputs of a lazily evaluated project, then its generators. A project reached but left out
    /// consumes files and generators exactly like any other, and a change to one of them has to
    /// reach the suites that see it through the facade.
    /// </remarks>
    private void Record(ProjectFacts facts)
    {
        _inputs[facts.ProjectPath] = facts.InputFiles;

        // Taken back before it is given, and review found the asymmetry: the line above *replaces*
        // what a project consumes, while the loop below only ever added what it generates from. A
        // project is recorded twice - once by the sweep, once for the framework its suites load -
        // and an analyzer reference the outer evaluation sees while the selected framework does not
        // (`Condition="'$(TargetFramework)' != 'net10.0'"`) was added by the first pass and never
        // withdrawn. A later change to that generator then widened a project whose compilation does
        // not consume it, and an unrelated survivor already sitting there fails the partial gate -
        // the one failure mode this selection is built to avoid.
        foreach ((string generator, List<string> consumers) in _analyzerConsumers)
        {
            if (!facts.AnalyzerProjects.Contains(generator, ProjectPaths.Comparer))
            {
                consumers.RemoveAll(
                    consumer => ProjectPaths.Comparer.Equals(consumer, facts.ProjectPath));
            }
        }

        foreach (string generator in facts.AnalyzerProjects)
        {
            if (!_analyzerConsumers.TryGetValue(generator, out List<string>? consumers))
            {
                _analyzerConsumers[generator] = consumers = [];
            }

            // Guarded because a project can be recorded twice: once from the sweep, once for the
            // framework its suites load. Widening is idempotent, so a duplicate changes no outcome,
            // but this list is answered to callers and a repeated consumer is not an answer.
            if (!consumers.Contains(facts.ProjectPath, ProjectPaths.Comparer))
            {
                consumers.Add(facts.ProjectPath);
            }
        }
    }

    /// <summary>
    /// What to do about a project MSBuild could not read: why every project is read, and the
    /// exclusion that leaves this one out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every project under the directory is read, because whether it is a test project is one of
    /// the things being read. One that cannot be - a compatibility probe pinned to an SDK the
    /// machine does not have, a missing workload - stopped the run with MSBuild's output and
    /// nothing else. On <c>Reefact/first-class-errors</c> that was <c>tools/floor-check</c>, outside
    /// the solution and referenced by no test, behind a nested <c>global.json</c> pinning SDK 8.0.100
    /// (#47).
    /// </para>
    /// <para>
    /// It still stops the run. Setting the project aside instead would silently drop its tests if it
    /// holds any, and an unread project cannot say whether it does. The way out is named, with the
    /// pattern to write, and a nested <c>global.json</c> is pointed at when there is one, since it is
    /// the usual reason a project reads fine in its own directory and fails here. The pattern is in
    /// double quotes, as the README writes it: <c>cmd.exe</c> passes single quotes through, and a
    /// pattern copied from the message would then match nothing (review of #57).
    /// </para>
    /// </remarks>
    internal static string WayOut(string projectPath, string searchDirectory)
    {
        string root = Path.GetFullPath(searchDirectory);
        string relative = Path.GetRelativePath(root, Path.GetFullPath(projectPath)).Replace('\\', '/');
        string? directory = Path.GetDirectoryName(relative)?.Replace('\\', '/');
        string pattern = string.IsNullOrEmpty(directory) ? relative : $"{directory}/*";

        string? pinned = null;

        for (string? current = Path.GetDirectoryName(Path.GetFullPath(projectPath));
             current is not null && current.Length > root.Length;
             current = Path.GetDirectoryName(current))
        {
            if (File.Exists(Path.Combine(current, "global.json")))
            {
                pinned = Path.GetRelativePath(root, Path.Combine(current, "global.json")).Replace('\\', '/');
                break;
            }
        }

        string why = pinned is null
            ? string.Empty
            : $" It sits under its own '{pinned}', which chooses the SDK it is read with.";

        return $"Every project under the directory is read, to find the test projects.{why} " +
               $"If '{relative}' is not under test, leave it out with --exclude \"{pattern}\", " +
               "or with \"exclude\" in killmutants.json.";
    }

    /// <summary>
    /// What a refusal for finding no test project adds when projects that reference xUnit have never
    /// been restored: the likelier cause, and the command that removes it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A test project is recognised by being an executable, and a project that does not say
    /// <c>&lt;OutputType&gt;Exe&lt;/OutputType&gt;</c> itself is often made one by a package:
    /// measured, a project referencing only <c>Microsoft.NET.Test.Sdk</c> evaluates as
    /// <c>Library</c> before <c>dotnet restore</c> and as <c>Exe</c> after it, because the property
    /// arrives through the imports NuGet generates. On a fresh clone of
    /// <c>Reefact/first-class-errors</c>, whose test projects are written that way, no test project
    /// was found at all, and the refusal pointed at the one project it had set aside, which was not
    /// the cause (#66).
    /// </para>
    /// <para>
    /// Said only of projects that reference an xUnit test application and have no restore result,
    /// and before what was set aside: it names the likelier cause first without claiming it is the
    /// only one.
    /// </para>
    /// </remarks>
    private static string WhatWasNotRestored(IReadOnlyList<ProjectFacts> projects)
    {
        string[] unrestored =
        [
            .. projects
                .Where(project => !project.IsTestSupport && project.ReferencesATestApplicationPackage && !project.IsRestored)
                .Select(project => project.Name)
                .Order(StringComparer.Ordinal),
        ];

        if (unrestored.Length == 0)
        {
            return string.Empty;
        }

        var message = new StringBuilder();

        message.AppendLine().AppendLine().Append(
            unrestored.Length.ToString(CultureInfo.InvariantCulture) +
            " project(s) reference xUnit but have not been restored, which is the " +
            "likelier cause: a test project is recognised by being an executable, and a package such as " +
            "Microsoft.NET.Test.Sdk only makes it one once NuGet has restored it. Run 'dotnet restore' " +
            "and try again:");

        foreach (string name in unrestored)
        {
            message.AppendLine().Append("  ").Append(name);
        }

        return message.ToString();
    }

    /// <summary>
    /// A refusal that ends discovery, followed by what it set aside on the way.
    /// </summary>
    /// <remarks>
    /// A project set aside is reported in the run's report, and a refusal means there is no report:
    /// without this, the one test project of a repository whose <c>global.json</c> names an SDK that
    /// is not installed would be reported as "no test project", and the reason MSBuild gave - the one
    /// the user can act on - would be lost (DEC0022).
    /// </remarks>
    internal string WithWhatWasSetAside(string refusal)
    {
        if (_setAside.Count == 0)
        {
            return refusal;
        }

        var message = new StringBuilder(refusal);

        message.AppendLine().AppendLine().Append(CultureInfo.InvariantCulture,
            $"{_setAside.Count} project(s) could not be read and were set aside, which may be why:");

        foreach (SetAside part in _setAside)
        {
            message.AppendLine().AppendLine().Append(part.Name).Append(": ").Append(part.Reason);
        }

        return message.ToString();
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

            ProjectFacts facts;

            try
            {
                facts = await _msBuild
                    .GetProjectFactsAsync(path, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ProjectAnalysisException unreadable) when (!cancellationToken.IsCancellationRequested)
            {
                // Set aside rather than stopping the run (DEC0022): the rest of the directory is as
                // readable as it was, and the report names this one with the reason and the way out.
                // Nothing is known of what it holds, which is why a project set aside here withholds
                // the run's score - it may have been the only suite reaching some code.
                _setAside.Add(new SetAside(
                    SetAsideStage.Discovery,
                    Path.GetRelativePath(searchDirectory, path).Replace('\\', '/'),
                    unreadable.Message + Environment.NewLine + WayOut(path, searchDirectory)));

                continue;
            }

            // A suite on several frameworks is read again for the one the developer chose: its
            // output, its references and its assembly are the chosen framework's, and the outer
            // build answers for none of them. Only a test project, and only when the choice is one it
            // offers - a value it does not offer is refused below, with the project named.
            if (facts.IsTestProject && facts.FrameworkToReadFor(_targetFramework) is { } framework)
            {
                bool several = facts.TargetsSeveralFrameworks;

                facts = await _msBuild
                    .GetProjectFactsAsync(path, framework, cancellationToken)
                    .ConfigureAwait(false);

                if (several)
                {
                    _builtFor[facts.ProjectPath] = framework;
                }
            }

            Record(facts);
            projects.Add(facts);
        }

        return projects;
    }

    /// <summary>
    /// The same rule as <see cref="RequireTheFrameworkTheyRunOn"/>, seen from the other end: a
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
            "KillMutants cannot mutate a project that its test suites reach from different " +
            "frameworks in one run, because each framework would have its own build and its own " +
            "score. Run one framework at a time with --target-framework <tfm>, and --exclude the " +
            "test projects that do not run on it:" +
            Environment.NewLine + details);
    }

    /// <summary>
    /// Every test project runs on the framework the developer named, or, when none was named, on the
    /// only one it offers. See DEC0021.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A test project on several frameworks would need one run per framework, with a separate build
    /// output and a separate verdict for each. Rather than silently picking one and reporting a score
    /// for a framework nobody chose, the run stops and says how to choose.
    /// </para>
    /// <para>
    /// Refused too: a test project that does not offer the chosen framework. Setting it aside would
    /// leave the code that only it tested out of the report without the score showing it, and
    /// <c>--exclude</c> already exists for setting a project aside on purpose.
    /// </para>
    /// </remarks>
    private void RequireTheFrameworkTheyRunOn(IReadOnlyList<ProjectFacts> testProjects)
    {
        if (_targetFramework is null)
        {
            ProjectFacts[] multiTargeted = [.. testProjects.Where(project => project.NeedsAFrameworkChoice)];

            if (multiTargeted.Length == 0)
            {
                return;
            }

            string details = string.Join(
                Environment.NewLine,
                multiTargeted.Select(project =>
                    $"  {project.Name}: {string.Join(", ", project.TargetFrameworks)}"));

            throw new ProjectAnalysisException(
                "A test project that targets several frameworks needs to be told which one to run on, " +
                "because each would have its own build, its own output and its own score. Pass " +
                "--target-framework <tfm> (or 'targetFramework' in killmutants.json) with one of:" +
                Environment.NewLine + details);
        }

        ProjectFacts[] elsewhere = [.. testProjects.Where(project =>
            !string.Equals(project.TargetFramework, _targetFramework, StringComparison.OrdinalIgnoreCase))];

        if (elsewhere.Length == 0)
        {
            return;
        }

        string offered = string.Join(
            Environment.NewLine,
            elsewhere.Select(project =>
                $"  {project.Name}: " +
                (project.TargetsSeveralFrameworks
                    ? string.Join(", ", project.TargetFrameworks)
                    : project.TargetFramework)));

        throw new ProjectAnalysisException(
            $"--target-framework {_targetFramework} asks for a framework these test projects do not " +
            "offer. Use --exclude to set a project aside on purpose, or name a framework it offers:" +
            Environment.NewLine + offered);
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
    /// <returns>
    /// The targets whose test projects all built. A target with one that did not is set aside with
    /// the build's output (DEC0022): its measurement would rest on a suite that is not there.
    /// </returns>
    public async Task<IReadOnlyList<MutationTestTarget>> BuildTestProjectsAsync(
        IReadOnlyList<MutationTestTarget> targets,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targets);

        Dictionary<string, string> failed = new(ProjectPaths.Comparer);

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

            try
            {
                await BuildAsync(projectPaths[index], cancellationToken).ConfigureAwait(false);
            }
            catch (ProjectAnalysisException broken) when (!cancellationToken.IsCancellationRequested)
            {
                failed[projectPaths[index]] = broken.Message;
            }
        }

        List<MutationTestTarget> built = [];

        foreach (MutationTestTarget target in targets)
        {
            string[] reasons =
            [
                .. target.TestProjects
                    .Where(test => failed.ContainsKey(test.ProjectPath))
                    .Select(test => failed[test.ProjectPath]),
            ];

            if (reasons.Length == 0)
            {
                built.Add(target);

                continue;
            }

            _setAside.Add(new SetAside(
                SetAsideStage.Build, target.ProjectUnderTest.Name, string.Join(Environment.NewLine, reasons)));
        }

        VerifyTestFramework(built);

        return built;
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

    private async Task RestoreAsync(ProjectFacts project, CancellationToken cancellationToken)
    {
        ProcessResult result = await ProcessRunner.RunAsync(
                "dotnet",
                ["restore", project.ProjectPath, $"-p:Configuration={_configuration}", "-nologo"],
                project.Directory,
                BuildBudget,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            throw new ProjectAnalysisException(
                $"'{project.Name}' failed to restore, so there is nothing to mutate." +
                $"{Environment.NewLine}{result.CombinedOutput}");
        }
    }

    private async Task BuildAsync(string projectPath, CancellationToken cancellationToken)
    {
        ProcessResult result = await ProcessRunner.RunAsync(
                "dotnet",
                _builtFor.TryGetValue(projectPath, out string? framework)
                    ? ["build", projectPath, "-c", _configuration, "-f", framework, "-nologo"]
                    : ["build", projectPath, "-c", _configuration, "-nologo"],
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
