using KillMutants.Selection;

namespace KillMutants.Core.Tests.Selection;

/// <summary>
/// Two shapes, and the difference between them is the whole of DEC0011's selection rule: a project
/// the change widened to, or a project only the changed lines of which are being judged.
/// </summary>
public class MutantSelectionTests
{
    [Fact]
    public void Everything_includes_every_file_without_being_asked_about_any()
    {
        Assert.True(MutantSelection.Everything.IsEverything);
        Assert.False(MutantSelection.Everything.IsEmpty);
        Assert.True(MutantSelection.Everything.Includes("/anywhere/at/all.cs"));
    }

    [Fact]
    public void A_list_includes_what_is_on_it_and_nothing_else()
    {
        MutantSelection selection = MutantSelection.Of([Local("src", "Money.cs")]);

        Assert.True(selection.Includes(Local("src", "Money.cs")));
        Assert.False(selection.Includes(Local("src", "Basket.cs")));
    }

    /// <summary>
    /// The same file named two ways is one file, because the filesystem says so.
    /// </summary>
    /// <remarks>
    /// git reports paths relative to the repository root; a syntax tree carries whatever absolute
    /// path the compiler was given. The two agree on the file and can disagree on how it is spelt.
    /// </remarks>
    [Fact]
    public void A_path_is_matched_the_way_the_filesystem_would()
    {
        MutantSelection selection = MutantSelection.Of([Local("src", "Money.cs")]);

        Assert.True(selection.Includes(Local("src", ".", "Money.cs")));
    }

    [Fact]
    public void An_empty_list_selects_nothing_and_says_so()
    {
        MutantSelection selection = MutantSelection.Of([]);

        Assert.True(selection.IsEmpty);
        Assert.False(selection.IsEverything);
        Assert.False(selection.Includes(Local("src", "Money.cs")));
    }

    private static string Local(params string[] parts) =>
        Path.Combine([Path.GetTempPath(), "repo", .. parts]);
}
