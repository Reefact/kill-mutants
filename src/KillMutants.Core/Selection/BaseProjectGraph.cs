using KillMutants.Projects;

namespace KillMutants.Selection;

/// <summary>
/// The project graph as it was before the change, read from a snapshot of that state.
/// </summary>
/// <remarks>
/// <para>
/// DEC0011 requires the widening relation to be read at both states, <c>targets(before) ∪
/// targets(now)</c>, and this is the earlier half. Remove the <c>ProjectReference</c> from
/// <c>Tests</c> to <c>ProjectA</c> in the very change being judged and the current graph no longer
/// says <c>Tests</c> exercises <c>ProjectA</c>: asking the current state alone is asking a question
/// whose answer the change has already deleted.
/// </para>
/// <para>
/// Read with MSBuild rather than by parsing the project files, because a <c>ProjectReference</c> can
/// come from a <c>Directory.Build.props</c>, a glob or a condition, and a graph that misses those is
/// a graph that quietly under-widens - which is the failure this whole mechanism exists to prevent.
/// MSBuild answers <c>-getItem:ProjectReference</c> from evaluation alone, so the export needs no
/// restore and no build: verified against the .NET 10 SDK on a tree with no <c>obj</c> directory
/// anywhere in it.
/// </para>
/// <para>
/// Projects are read one at a time and cached, walking out from the test project asked about, so a
/// codebase pays for the part of its graph the change actually touches rather than for all of it.
/// A change with nothing test-side in it never constructs this at all.
/// </para>
/// </remarks>
internal sealed class BaseProjectGraph : IDisposable
{
    private readonly MsBuildQuery _msBuild;
    private readonly ICodeSnapshot _snapshot;
    private readonly string _root;
    private readonly string _label;
    private readonly Dictionary<(string Project, string Framework), ProjectFacts?> _facts;

    private BaseProjectGraph(
        MsBuildQuery msBuild,
        ICodeSnapshot snapshot,
        string label,
        IReadOnlyCollection<string> projectFiles)
    {
        _msBuild = msBuild;
        _snapshot = snapshot;
        _root = snapshot.Root;
        _label = label;
        _facts = [];
        ProjectFiles = new HashSet<string>(projectFiles, RelativePath.Comparer);
    }

    /// <summary>Every C# project that existed before the change, by relative path.</summary>
    public IReadOnlySet<string> ProjectFiles { get; }

    /// <summary>Indexes the projects of a snapshot of the code as it was.</summary>
    /// <param name="snapshot">
    /// The code before the change, already laid out. Owned from here on: disposing the graph
    /// disposes it.
    /// </param>
    /// <param name="label">What to call that state in a message.</param>
    /// <param name="configuration">The build configuration to evaluate against.</param>
    public static BaseProjectGraph Open(ICodeSnapshot snapshot, string label, string configuration)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        try
        {
            string[] projects = [.. Directory
                .EnumerateFiles(snapshot.Root, "*.csproj", SearchOption.AllDirectories)
                .Select(path => RelativePath.Of(snapshot.Root, path))
                .Where(path => path is not null)
                .Select(path => path!)
                .Order(StringComparer.Ordinal)];

            return new BaseProjectGraph(new MsBuildQuery(configuration), snapshot, label, projects);
        }
        catch
        {
            snapshot.Dispose();

            throw;
        }
    }

    /// <summary>True when the project at that path was a test project before the change.</summary>
    public async Task<bool> IsTestProjectAsync(
        string relativePath,
        CancellationToken cancellationToken = default) =>
        await FactsOfAsync(relativePath, null, cancellationToken).ConfigureAwait(false)
            is { IsTestProject: true };

    /// <summary>
    /// Every mutable project a test project reached before the change, by relative path.
    /// </summary>
    /// <remarks>
    /// The same traversal as <see cref="ProjectDiscovery"/>'s, deliberately: other test projects are
    /// not targets, a declared test-support library is walked through rather than returned, and a
    /// project that has since disappeared is simply not in the current graph to select. What this
    /// cannot know is which projects the run excludes - that is a property of the run, not of the
    /// state - so an excluded project can be named here and is dropped when the answer is matched
    /// against the current targets.
    /// </remarks>
    public async Task<IReadOnlyList<string>> ProductionProjectsReachedFromAsync(
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        ProjectFacts? testProject = await FactsOfAsync(relativePath, null, cancellationToken)
            .ConfigureAwait(false);

        if (testProject is null)
        {
            return [];
        }

        // The framework the suite loads, carried through everything it reaches, and review found it
        // missing. The same measured behaviour the current side already answers for: evaluated
        // without a TargetFramework, the outer build of a multi-targeted project has that property
        // empty and every item conditioned on it is absent - so a `ProjectReference` written
        // `Condition="'$(TargetFramework)' == 'net10.0'"` simply is not there. Reading the earlier
        // state that way lost the edge silently: `Tests -> Facade -> Core` then, `Tests -> Facade`
        // now, and nothing to say Core had ever been reached.
        //
        // Empty when the suite is itself multi-targeted, which discovery refuses on the current
        // side; there is no framework to carry then, and the outer build is what there is.
        string? framework = string.IsNullOrEmpty(testProject.TargetFramework)
            ? null
            : testProject.TargetFramework;

        List<string> reached = [];
        HashSet<string> seen = new(RelativePath.Comparer);
        Queue<string> pending = new(Relative(testProject.ProjectReferences));

        while (pending.Count > 0)
        {
            string path = pending.Dequeue();

            if (!seen.Add(path))
            {
                continue;
            }

            ProjectFacts? facts = await FactsOfAsync(path, framework, cancellationToken)
                .ConfigureAwait(false);

            if (facts is null || facts.IsTestProject)
            {
                continue;
            }

            // A hole, not a wall - the same rule discovery applies, and for the third time in this
            // codebase. A project declaring itself test support is walked through and not returned:
            // review found that returning it made the coverage-loss check report a support library
            // as having lost its tests, and fail the gate, over a project that was never a target to
            // begin with.
            if (!facts.IsTestSupport)
            {
                reached.Add(path);
            }

            foreach (string reference in Relative(facts.ProjectReferences))
            {
                pending.Enqueue(reference);
            }
        }

        return reached;
    }

    /// <summary>Gives the snapshot back.</summary>
    public void Dispose() => _snapshot.Dispose();

    /// <summary>What the snapshot's copy of a project evaluates to, read once per framework.</summary>
    /// <remarks>
    /// Cached per framework as well as per project, because the same project answers differently
    /// under each and the two answers must not overwrite one another. A null framework asks for the
    /// project's outer build, which is what a suite itself is read on.
    /// </remarks>
    private async Task<ProjectFacts?> FactsOfAsync(
        string relativePath,
        string? targetFramework,
        CancellationToken cancellationToken)
    {
        (string, string) key = (relativePath, targetFramework ?? string.Empty);

        if (_facts.TryGetValue(key, out ProjectFacts? known))
        {
            return known;
        }

        ProjectFacts? facts = null;

        // A project inside a component the snapshot could not lay out is not "there was no such
        // project": it is a question this comparison cannot answer. Reading the absence as an
        // answer would drop the edge that ran through it and go green over coverage never checked.
        // Everything else is what that state held - a snapshot restores code, it does not filter it -
        // so an absence anywhere else is a real absence.
        if (!ProjectFiles.Contains(relativePath) &&
            _snapshot.Missing.Any(part => RelativePath.IsUnder(relativePath, part)))
        {
            throw new ChangeSelectionException(
                $"'{relativePath}' belongs to a component this run could not read as it was at " +
                $"{Short(_label)}, so KillMutants cannot tell which projects that state's tests " +
                "exercised. A component whose contents live elsewhere has to be present " +
                "locally for a partial run to compare against it. Run without --since to measure " +
                "the whole codebase instead.");
        }

        if (ProjectFiles.Contains(relativePath))
        {
            try
            {
                facts = await _msBuild
                    .GetProjectFactsAsync(
                        RelativePath.In(_root, relativePath),
                        targetFramework,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ProjectAnalysisException exception)
            {
                // Not falling back to the current state, which is the whole point: a partial run
                // whose earlier side could not be read would widen too little, and would look
                // exactly like a run that had nothing to widen. DEC0011 says such a run is not to
                // be trusted.
                throw new ChangeSelectionException(
                    $"KillMutants could not read '{relativePath}' as it was at {Short(_label)}, " +
                    "so it cannot tell which projects that state's tests exercised. A partial run " +
                    "needs both states to be readable; run without --since to measure the whole " +
                    $"codebase instead.{Environment.NewLine}{exception.Message}",
                    exception);
            }
        }

        _facts[key] = facts;

        return facts;
    }

    /// <summary>Turns the absolute paths MSBuild answers with back into relative names.</summary>
    private IEnumerable<string> Relative(IEnumerable<string> absolute) =>
        absolute.Select(path => RelativePath.Of(_root, path)).OfType<string>();

    /// <summary>Cuts a state's name down for a message, without pretending to understand it.</summary>
    private static string Short(string label) =>
        label.Length > 12 ? label[..12] : label;
}
