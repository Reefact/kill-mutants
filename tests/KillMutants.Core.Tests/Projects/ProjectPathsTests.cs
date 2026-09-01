using KillMutants.Projects;

namespace KillMutants.Core.Tests.Projects;

/// <summary>
/// Discovery matches a project reference against the projects it found by enumerating directories,
/// and nothing makes the two agree on case. Where the filesystem says they are the same file, so
/// must we, or a valid reference resolves to nothing and takes the whole subtree behind it.
/// </summary>
/// <remarks>
/// The rule takes the platform as an argument precisely so it can be tested on any platform. Running
/// this on Linux against a real case-insensitive volume is not something a test can arrange, and
/// asserting only what the current platform does would leave the other two untested forever.
/// </remarks>
public class ProjectPathsTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Where_the_filesystem_ignores_case_so_do_we(bool windows, bool macOS)
    {
        Assert.True(Finds(ProjectPaths.ComparerFor(windows, macOS)));
    }

    /// <summary>
    /// And where it does not, two paths differing in case are two files, and merging them would be
    /// the same defect in the other direction.
    /// </summary>
    [Fact]
    public void Where_it_does_not_neither_do_we()
    {
        Assert.False(Finds(ProjectPaths.ComparerFor(windows: false, macOS: false)));
    }

    [Fact]
    public void The_rule_this_run_uses_is_the_one_its_platform_asks_for()
    {
        Assert.Same(
            ProjectPaths.ComparerFor(OperatingSystem.IsWindows(), OperatingSystem.IsMacOS()),
            ProjectPaths.Comparer);
    }

    /// <summary>Looks up a reference written in a different case from the enumerated path.</summary>
    private static bool Finds(StringComparer comparer)
    {
        Dictionary<string, string> byPath = new(comparer)
        {
            ["/repo/src/Core/Core.csproj"] = "Core",
        };

        return byPath.ContainsKey("/repo/src/core/core.csproj");
    }
}
