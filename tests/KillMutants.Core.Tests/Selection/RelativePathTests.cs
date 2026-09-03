using KillMutants.Selection;

namespace KillMutants.Core.Tests.Selection;

/// <summary>
/// A partial run compares two trees that are not in the same place on disk, so a project can only be
/// recognised across them by the name the repository gives it.
/// </summary>
public class RelativePathTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "repo"));

    [Fact]
    public void A_path_inside_the_repository_is_named_relative_to_it_with_forward_slashes()
    {
        Assert.Equal(
            "src/Core/Money.cs",
            RelativePath.Of(Root, Path.Combine(Root, "src", "Core", "Money.cs")));
    }

    /// <summary>
    /// Outside the repository is not a name to be normalised; it is an answer of "not here".
    /// </summary>
    /// <remarks>
    /// <c>Path.GetRelativePath</c> climbs out with <c>..</c> and, on Windows, returns the path
    /// unchanged when the two are on different volumes. Both mean the same thing and both must be
    /// null, or a file from somewhere else would be attributed to a project in this tree.
    /// </remarks>
    [Fact]
    public void A_path_outside_the_repository_has_no_repository_name()
    {
        Assert.Null(RelativePath.Of(Root, Path.Combine(Path.GetTempPath(), "elsewhere", "Money.cs")));
    }

    [Fact]
    public void The_directory_of_a_repository_name_is_everything_before_the_last_slash()
    {
        Assert.Equal("src/Core", RelativePath.DirectoryOf("src/Core/Money.cs"));
        Assert.Equal(string.Empty, RelativePath.DirectoryOf("global.json"));
    }

    [Fact]
    public void Everything_is_under_the_top_of_the_repository()
    {
        Assert.True(RelativePath.IsUnder("src/Core/Money.cs", string.Empty));
    }

    [Fact]
    public void A_path_is_under_a_directory_it_actually_sits_in()
    {
        Assert.True(RelativePath.IsUnder("src/Core/Money.cs", "src/Core"));
        Assert.True(RelativePath.IsUnder("src/Core/Money.cs", "src"));
    }

    /// <summary>
    /// Whole segments only, which a prefix comparison would get wrong.
    /// </summary>
    /// <remarks>
    /// <c>src/CoreTests/Thing.cs</c> starts with <c>src/Core</c>. Attributing it there would credit a
    /// change in one project to another, and on the test side that means widening the wrong suite.
    /// </remarks>
    [Fact]
    public void A_sibling_whose_name_merely_starts_the_same_is_not_under_it()
    {
        Assert.False(RelativePath.IsUnder("src/CoreTests/Thing.cs", "src/Core"));
    }

    [Fact]
    public void A_directory_is_not_under_itself_as_a_file()
    {
        Assert.False(RelativePath.IsUnder("src/Core", "src/Core"));
    }

    /// <summary>
    /// The parent segment, not the two characters that spell it.
    /// </summary>
    /// <remarks>
    /// Review found this: a directory legally named <c>..tests</c> produces a relative path starting
    /// with <c>..</c> without climbing anywhere, and was read as outside the repository. A test
    /// project there was then left out of the base-side traversal, so a removed project reference
    /// could go unreported.
    /// </remarks>
    [Fact]
    public void A_directory_whose_name_begins_with_two_dots_is_still_in_the_repository()
    {
        string root = Path.Combine(Path.GetTempPath(), "repo");

        Assert.Equal(
            "..tests/Suite.csproj",
            RelativePath.Of(root, Path.Combine(root, "..tests", "Suite.csproj")));
    }

    [Fact]
    public void A_path_that_really_climbs_out_is_still_outside_the_repository()
    {
        string root = Path.Combine(Path.GetTempPath(), "repo");

        Assert.Null(RelativePath.Of(root, Path.Combine(Path.GetTempPath(), "elsewhere", "Suite.csproj")));
    }
}
