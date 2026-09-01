using KillMutants.Filtering;

namespace KillMutants.Core.Tests.Filtering;

public class PathFilterTests
{
    private const string Root = "/work/repo";

    private static PathFilter Excluding(params string[] patterns) =>
        PathFilter.Excluding(Root, patterns);

    [Fact]
    public void A_filter_with_no_patterns_excludes_nothing()
    {
        Assert.False(PathFilter.None.Excludes("/work/repo/src/A/A.csproj"));
        Assert.False(Excluding().Excludes("/work/repo/src/A/A.csproj"));
    }

    [Theory]
    [InlineData("tests/fixtures/*", "/work/repo/tests/fixtures/single/S.csproj", true)]
    [InlineData("tests/fixtures/*", "/work/repo/tests/Real.Tests/Real.Tests.csproj", false)]
    [InlineData("*.Generated.cs", "/work/repo/src/A/Model.Generated.cs", true)]
    [InlineData("src/A/*", "/work/repo/src/A/Model.cs", true)]
    [InlineData("src/A/*", "/work/repo/src/B/Model.cs", false)]
    public void A_pattern_is_matched_against_the_path_relative_to_the_root(
        string pattern, string path, bool excluded)
    {
        Assert.Equal(excluded, Excluding(pattern).Excludes(path));
    }

    /// <summary>
    /// The one surprise in this matcher, measured rather than assumed: <c>*</c> matches directory
    /// separators too, so <c>tests/*</c> covers everything beneath <c>tests</c> however deep. The
    /// help text says so, and this test is what fails if the matcher is ever swapped for one with
    /// the stricter glob semantics people may expect.
    /// </summary>
    [Fact]
    public void A_star_matches_across_directories()
    {
        Assert.True(Excluding("tests/*").Excludes("/work/repo/tests/a/b/c/Deep.csproj"));
    }

    [Fact]
    public void Any_one_pattern_is_enough_to_exclude()
    {
        PathFilter filter = Excluding("src/A/*", "src/B/*");

        Assert.True(filter.Excludes("/work/repo/src/A/A.csproj"));
        Assert.True(filter.Excludes("/work/repo/src/B/B.csproj"));
        Assert.False(filter.Excludes("/work/repo/src/C/C.csproj"));
    }

    /// <summary>
    /// Paths come from MSBuild and from the file system; a user types patterns by hand. Matching
    /// without regard to case is the forgiving choice, and costs nothing here.
    /// </summary>
    [Fact]
    public void Matching_ignores_case()
    {
        Assert.True(Excluding("TESTS/Fixtures/*").Excludes("/work/repo/tests/fixtures/S.csproj"));
    }

    /// <summary>
    /// A path outside the directory the run was pointed at cannot match a relative pattern: it
    /// relativises to something starting with '..', which no ordinary pattern describes.
    /// </summary>
    [Fact]
    public void A_path_outside_the_root_is_not_excluded()
    {
        Assert.False(Excluding("src/*").Excludes("/elsewhere/src/A.csproj"));
    }

    [Fact]
    public void An_empty_pattern_is_rejected_rather_than_silently_matching()
    {
        Assert.Throws<ArgumentException>(() => PathFilter.Excluding(Root, ["src/*", "  "]));
    }
}
