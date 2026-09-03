using KillMutants.Projects;

namespace KillMutants.Selection;

/// <summary>
/// The project graph as it was at the base revision, read from a throwaway export of that tree.
/// </summary>
/// <remarks>
/// <para>
/// DEC0011 requires the widening relation to be read at both revisions, <c>targets(base) ∪
/// targets(head)</c>, and this is the base half. Remove the <c>ProjectReference</c> from
/// <c>Tests</c> to <c>ProjectA</c> in the very change being judged and the HEAD graph no longer says
/// <c>Tests</c> exercises <c>ProjectA</c>: asking HEAD alone is asking a question whose answer the
/// change has already deleted.
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
/// repository pays for the part of its graph the change actually touches rather than for all of it.
/// A change with nothing test-side in it never constructs this at all.
/// </para>
/// </remarks>
internal sealed class BaseProjectGraph : IDisposable
{
    private readonly MsBuildQuery _msBuild;
    private readonly ICodeSnapshot _snapshot;
    private readonly string _root;
    private readonly string _revision;
    private readonly Dictionary<string, ProjectFacts?> _facts;

    private BaseProjectGraph(
        MsBuildQuery msBuild,
        ICodeSnapshot snapshot,
        string revision,
        IReadOnlyCollection<string> projectFiles)
    {
        _msBuild = msBuild;
        _snapshot = snapshot;
        _root = snapshot.Root;
        _revision = revision;
        _facts = [];
        ProjectFiles = new HashSet<string>(projectFiles, RepositoryPath.Comparer);
    }

    /// <summary>Every C# project that existed at the base revision, by repository path.</summary>
    public IReadOnlySet<string> ProjectFiles { get; }

    /// <summary>Indexes the projects of a snapshot of the code as it was.</summary>
    /// <param name="snapshot">
    /// The code before the change, already laid out. Owned from here on: disposing the graph
    /// disposes it.
    /// </param>
    /// <param name="revision">What to call that state in a message.</param>
    /// <param name="configuration">The build configuration to evaluate against.</param>
    public static BaseProjectGraph Open(ICodeSnapshot snapshot, string revision, string configuration)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        try
        {
            string[] projects = [.. Directory
                .EnumerateFiles(snapshot.Root, "*.csproj", SearchOption.AllDirectories)
                .Select(path => RepositoryPath.Of(snapshot.Root, path))
                .Where(path => path is not null)
                .Select(path => path!)
                .Order(StringComparer.Ordinal)];

            return new BaseProjectGraph(new MsBuildQuery(configuration), snapshot, revision, projects);
        }
        catch
        {
            snapshot.Dispose();

            throw;
        }
    }

    /// <summary>True when the project at that path was a test project at the base revision.</summary>
    public async Task<bool> IsTestProjectAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default) =>
        await FactsOfAsync(repositoryPath, cancellationToken).ConfigureAwait(false)
            is { IsTestProject: true };

    /// <summary>
    /// Every mutable project a test project reached at the base revision, by repository path.
    /// </summary>
    /// <remarks>
    /// The same traversal as <see cref="ProjectDiscovery"/>'s, deliberately: other test projects are
    /// not targets, a declared test-support library is walked through rather than returned, and a
    /// project that has since disappeared is simply not in the head graph to select. What this
    /// cannot know is which projects the run excludes - that is a property of the
    /// run, not of the revision - so an excluded project can be named here and is dropped when the
    /// answer is matched against the head targets.
    /// </remarks>
    public async Task<IReadOnlyList<string>> ProductionProjectsReachedFromAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        ProjectFacts? testProject = await FactsOfAsync(repositoryPath, cancellationToken)
            .ConfigureAwait(false);

        if (testProject is null)
        {
            return [];
        }

        List<string> reached = [];
        HashSet<string> seen = new(RepositoryPath.Comparer);
        Queue<string> pending = new(Relative(testProject.ProjectReferences));

        while (pending.Count > 0)
        {
            string path = pending.Dequeue();

            if (!seen.Add(path))
            {
                continue;
            }

            ProjectFacts? facts = await FactsOfAsync(path, cancellationToken).ConfigureAwait(false);

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

    private async Task<ProjectFacts?> FactsOfAsync(
        string repositoryPath,
        CancellationToken cancellationToken)
    {
        if (_facts.TryGetValue(repositoryPath, out ProjectFacts? known))
        {
            return known;
        }

        ProjectFacts? facts = null;

        // A project inside a component the snapshot could not lay out is not "there was no such
        // project": it is a question this comparison cannot answer. Reading the absence as an
        // answer would drop the edge that ran through it and go green over coverage never checked.
        // Everything else is what that state held - a snapshot is a checkout, not a filtered copy -
        // so an absence anywhere else is a real absence.
        if (!ProjectFiles.Contains(repositoryPath) &&
            _snapshot.Missing.Any(part => RepositoryPath.IsUnder(repositoryPath, part)))
        {
            throw new ChangeSelectionException(
                $"'{repositoryPath}' belongs to a component this run could not read as it was at " +
                $"{Short(_revision)}, so KillMutants cannot tell which projects that state's tests " +
                "exercised. A component whose contents live in another repository has to be present " +
                "locally for a partial run to compare against it. Run without --since to measure " +
                "the whole codebase instead.");
        }

        if (ProjectFiles.Contains(repositoryPath))
        {
            try
            {
                facts = await _msBuild
                    .GetProjectFactsAsync(
                        RepositoryPath.In(_root, repositoryPath), cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ProjectAnalysisException exception)
            {
                // Not falling back to HEAD, which is the whole point: a partial run whose base graph
                // could not be read would widen too little, and would look exactly like a run that
                // had nothing to widen. DEC0011 says such a run is not to be trusted.
                throw new ChangeSelectionException(
                    $"KillMutants could not read '{repositoryPath}' as it was at {Short(_revision)}, " +
                    "so it cannot tell which projects that revision's tests exercised. A partial run " +
                    "needs both revisions to be readable; run without --since to measure the whole " +
                    $"codebase instead.{Environment.NewLine}{exception.Message}",
                    exception);
            }
        }

        _facts[repositoryPath] = facts;

        return facts;
    }

    /// <summary>Turns the absolute paths MSBuild answers with back into repository names.</summary>
    private IEnumerable<string> Relative(IEnumerable<string> absolute) =>
        absolute.Select(path => RepositoryPath.Of(_root, path)).OfType<string>();

    private static string Short(string revision) =>
        revision.Length > 8 ? revision[..8] : revision;
}
