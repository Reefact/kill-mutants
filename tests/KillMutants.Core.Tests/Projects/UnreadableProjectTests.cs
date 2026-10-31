using KillMutants.Projects;

namespace KillMutants.Core.Tests.Projects;

/// <summary>
/// Regression tests (#47). A project MSBuild cannot read stops discovery; the message now says why
/// it was read at all, names the exclusion that leaves it out, and points at a nested
/// <c>global.json</c> when there is one.
/// </summary>
public sealed class UnreadableProjectTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("killmutants-wayout-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void The_exclusion_to_write_is_named_with_the_projects_directory()
    {
        string project = Create("tools/floor-check/FloorCheck.csproj");

        string wayOut = ProjectDiscovery.WayOut(project, _root);

        Assert.Contains("--exclude 'tools/floor-check/*'", wayOut, StringComparison.Ordinal);
        Assert.Contains("'tools/floor-check/FloorCheck.csproj'", wayOut, StringComparison.Ordinal);
        Assert.DoesNotContain("global.json", wayOut, StringComparison.Ordinal);
    }

    [Fact]
    public void A_nested_global_json_is_pointed_at()
    {
        string project = Create("tools/floor-check/FloorCheck.csproj");
        File.WriteAllText(Path.Combine(_root, "tools", "floor-check", "global.json"), "{}");

        Assert.Contains("'tools/floor-check/global.json'", ProjectDiscovery.WayOut(project, _root), StringComparison.Ordinal);
    }

    /// <summary>The repository's own <c>global.json</c> is not the reason one project fails, so it is not named.</summary>
    [Fact]
    public void The_global_json_beside_the_searched_directory_is_not_blamed()
    {
        string project = Create("src/Library/Library.csproj");
        File.WriteAllText(Path.Combine(_root, "global.json"), "{}");

        Assert.DoesNotContain("global.json", ProjectDiscovery.WayOut(project, _root), StringComparison.Ordinal);
    }

    [Fact]
    public void A_project_at_the_root_is_excluded_by_its_own_path()
    {
        string project = Create("Probe.csproj");

        Assert.Contains("--exclude 'Probe.csproj'", ProjectDiscovery.WayOut(project, _root), StringComparison.Ordinal);
    }

    private string Create(string relative)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "<Project />");

        return path;
    }
}
