namespace KillMutants.Projects;

/// <summary>
/// Everything discovery learned about a repository, for a caller that needs more than the targets.
/// </summary>
/// <param name="Targets">Each project to mutate, paired with the suites that exercise it.</param>
/// <param name="TestProjectPaths">
/// Every test project discovery recognised, including one that exercises nothing - which is in no
/// target, and which a change may be what emptied.
/// </param>
/// <param name="LeftOut">
/// Projects a suite reaches that this run deliberately does not mutate, each with the suites that
/// reach it. Excluded by the user, or declaring themselves test support: not a target, and not an
/// accident either.
/// </param>
/// <param name="Inputs">
/// What each project consumes, by project path, empty unless discovery was asked to read it.
/// </param>
/// <param name="AnalyzerConsumers">
/// Which projects consume each generator project, by the generator's path. A generator is referenced
/// as an analyzer, so it is neither a target nor anything a target links - and it still decides what
/// its consumers compile.
/// </param>
/// <remarks>
/// A full run needs only <paramref name="Targets"/>, which is what discovery returned for eleven
/// milestones. A partial run needs the rest, and needs it about every project rather than only the
/// measured ones: it has to attribute a changed file to whatever builds it, and "whatever builds it"
/// includes a suite and a declared helper as readily as a project under test.
/// </remarks>
internal sealed record DiscoveredProjects(
    IReadOnlyList<MutationTestTarget> Targets,
    IReadOnlySet<string> TestProjectPaths,
    IReadOnlyDictionary<string, IReadOnlyList<string>> LeftOut,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Inputs,
    IReadOnlyDictionary<string, IReadOnlyList<string>> AnalyzerConsumers)
{
    /// <summary>The projects this run mutates, by path.</summary>
    public IReadOnlySet<string> TargetPaths { get; } = new HashSet<string>(
        Targets.Select(target => target.ProjectUnderTest.ProjectPath), ProjectPaths.Comparer);

    /// <summary>Every project discovery has a path for, whatever role it plays.</summary>
    public IEnumerable<string> AllProjects =>
        TargetPaths
            .Concat(TestProjectPaths)
            .Concat(LeftOut.Keys)
            .Concat(AnalyzerConsumers.Keys)
            .Distinct(ProjectPaths.Comparer);
}
