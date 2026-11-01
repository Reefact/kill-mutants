using KillMutants.Filtering;

namespace KillMutants.Core.Tests.Filtering;

/// <summary>
/// DEC0025: the untracked files a series may ignore, and the patterns it refuses because they could
/// cover a file that changes what is built.
/// </summary>
public class UntrackedPatternsTests
{
    /// <summary>The files #66 found, named the way the record says a project should name them.</summary>
    [Theory]
    [InlineData("*.received.html")]
    [InlineData("*.received.json")]
    [InlineData("*.received.txt")]
    [InlineData("tests/Snapshots/*.received.html")]
    [InlineData("TestResults/*.trx")]
    public void A_pattern_that_names_its_files_is_accepted(string pattern)
    {
        UntrackedPatterns.Validate([pattern]);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("**")]
    [InlineData("*/*")]
    [InlineData("?*")]
    [InlineData("*?")]
    public void A_pattern_made_only_of_wildcards_is_refused_for_what_it_is(string pattern)
    {
        var refused = Assert.Throws<ArgumentException>(() => UntrackedPatterns.Validate([pattern]));

        Assert.Contains("every untracked file", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The overlap is decided, not sampled: <c>a*.cs</c> accepts no fixed sample name such as
    /// <c>x.cs</c>, and still covers <c>abc.cs</c>.
    /// </summary>
    [Theory]
    [InlineData("*.*", "*.cs")]
    [InlineData("*.cs", "*.cs")]
    [InlineData("a*.cs", "*.cs")]
    [InlineData("src/Generated/*", "*.cs")]
    [InlineData("*.received.*", "*.cs")]
    [InlineData("*.json", "global.json")]
    [InlineData("*.config", "NuGet.config")]
    [InlineData("tools/*/global.json", "*/global.json")]
    [InlineData("*.c?", "*.cs")]
    public void A_pattern_that_could_cover_a_build_file_is_refused(string pattern, string covered)
    {
        var refused = Assert.Throws<ArgumentException>(() => UntrackedPatterns.Validate([pattern]));

        Assert.Contains($"'{covered}'", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>Windows compiles <c>Model.CS</c>, so case is ignored whatever the platform.</summary>
    [Theory]
    [InlineData("*.CS")]
    [InlineData("SRC/*.Cs")]
    [InlineData("*.CsProj")]
    public void Case_does_not_get_a_pattern_past_the_list(string pattern)
    {
        Assert.Throws<ArgumentException>(() => UntrackedPatterns.Validate([pattern]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void An_empty_pattern_is_refused(string pattern)
    {
        Assert.Throws<ArgumentException>(() => UntrackedPatterns.Validate([pattern]));
    }

    [Theory]
    [InlineData("*.received.html", "*.cs", false)]
    [InlineData("*.received.*", "*.cs", true)]
    [InlineData("a?c", "abc", true)]
    [InlineData("a?c", "abbc", false)]
    [InlineData("*x*", "*y*", true)]
    [InlineData("x*", "y*", false)]
    [InlineData("*.cs", "*.csproj", false)]
    [InlineData(".editorconfig", "*/.editorconfig", false)]
    public void Two_patterns_overlap_when_some_path_matches_both(string left, string right, bool overlap)
    {
        Assert.Equal(overlap, UntrackedPatterns.Overlap(left, right));
        Assert.Equal(overlap, UntrackedPatterns.Overlap(right, left));
    }

    [Theory]
    [InlineData("*.received.html", "FirstClassErrors.GenDoc.UnitTests/Renderer.Html.received.html", true)]
    [InlineData("*.received.html", "Renderer.Html.received.json", false)]
    [InlineData("tests/*.received.html", "tests/a/b/Deep.received.html", true)]
    [InlineData("tests/*.received.html", "src/Deep.received.html", false)]
    [InlineData("*.RECEIVED.html", "x.received.html", true)]
    public void A_path_is_matched_as_exclude_matches_it(string pattern, string path, bool matched)
    {
        Assert.Equal(matched, UntrackedPatterns.Matches([pattern], path));
    }
}
