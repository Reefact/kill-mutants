using KillMutants.Projects;

namespace KillMutants.Core.Tests.Projects;

/// <summary>
/// The mirror of the rule that refuses a multi-targeted test project: a library reached by suites
/// running on different frameworks.
/// </summary>
/// <remarks>
/// <para>
/// Only the first framework used to be kept, and both suites were attached to the same target. The
/// run compiled one variant of the library and injected it into both test outputs, so one suite was
/// measured against an assembly it does not reference.
/// </para>
/// <para>
/// The rule takes the grouping as an argument rather than reading it from a solution on disk.
/// Building the real thing needs two test applications on two runtimes, which is a fixture this
/// repository cannot host on one SDK - and would test MSBuild rather than the rule.
/// </para>
/// </remarks>
public class MixedFrameworkTargetTests
{
    [Fact]
    public void A_project_reached_from_two_frameworks_is_refused_by_name()
    {
        ProjectAnalysisException refusal = Assert.Throws<ProjectAnalysisException>(
            () => ProjectDiscovery.RejectProjectsReachedFromSeveralFrameworks(
                Grouping(("/repo/src/Core/Core.csproj", ["net10.0", "net9.0"]))));

        Assert.Contains("Core", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("net9.0", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("net10.0", refusal.Message, StringComparison.Ordinal);

        // Says what it would take rather than only what it will not do.
        Assert.Contains("its own run and its own score", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// One framework reached by several suites is the ordinary case and must stay silent, or every
    /// repository with two test projects over one library would be refused.
    /// </summary>
    [Fact]
    public void Several_suites_on_one_framework_are_ordinary()
    {
        ProjectDiscovery.RejectProjectsReachedFromSeveralFrameworks(
            Grouping(
                ("/repo/src/Core/Core.csproj", ["net10.0"]),
                ("/repo/src/Domain/Domain.csproj", ["net10.0"])));
    }

    [Fact]
    public void Nothing_reached_at_all_is_not_a_conflict()
    {
        ProjectDiscovery.RejectProjectsReachedFromSeveralFrameworks(Grouping());
    }

    private static Dictionary<string, SortedSet<string>> Grouping(
        params (string Project, string[] Frameworks)[] entries) =>
        entries.ToDictionary(
            entry => entry.Project,
            entry => new SortedSet<string>(entry.Frameworks, StringComparer.Ordinal),
            StringComparer.Ordinal);
}
