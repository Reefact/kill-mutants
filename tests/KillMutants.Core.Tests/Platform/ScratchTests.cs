namespace KillMutants.Core.Tests.Platform;

/// <summary>
/// The directory a run works in is named from the kill-mutants theme and created under the temporary
/// path. What is pinned here is the contract its two callers rely on - the sandboxes' root and the
/// base revision's worktree - rather than the words: a directory that exists, that nothing else owns,
/// and a search that gives up rather than spin.
/// </summary>
public sealed class ScratchTests : IDisposable
{
    private readonly string _parent = Directory.CreateTempSubdirectory("killmutants-test-").FullName;
    private readonly List<string> _created = [];

    public void Dispose()
    {
        foreach (string directory in _created)
        {
            Scratch.DeleteDirectory(directory);
        }

        Directory.Delete(_parent, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_run_directory_is_created_under_the_temporary_path()
    {
        string directory = Created();

        Assert.True(Directory.Exists(directory));
        Assert.Equal(
            Path.TrimEndingDirectorySeparator(Path.GetTempPath()),
            Path.GetDirectoryName(directory));
    }

    /// <summary>
    /// Two or three lowercase words joined by hyphens, and no longer than the GUID name it replaced:
    /// the shape the theme promises, not any particular word, which a seed would pin and the shared
    /// random source does not.
    /// </summary>
    [Fact]
    public void The_name_is_a_phrase_from_the_theme_no_wider_than_the_guid_it_replaced()
    {
        string name = Path.GetFileName(Created());

        string[] words = name.Split('-');

        Assert.InRange(words.Length, 2, 3);
        Assert.All(words, word => Assert.True(
            word.Length > 0 && word.All(char.IsAsciiLetterLower), $"'{name}' is not a phrase of lowercase words."));
        Assert.InRange(name.Length, 1, ScratchNames.MaxLength);
    }

    [Fact]
    public void Two_run_directories_never_share_a_name()
    {
        string first = Created();
        string second = Created();

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void A_name_already_taken_is_drawn_again_rather_than_shared()
    {
        Directory.CreateDirectory(Path.Combine(_parent, "taken"));
        Queue<string> draws = new(["taken", "taken", "free"]);

        string directory = Scratch.CreateDirectory(_parent, draws.Dequeue);

        Assert.Equal(Path.Combine(_parent, "free"), directory);
        Assert.True(Directory.Exists(directory));
        Assert.Empty(draws);
    }

    /// <summary>
    /// A directory cannot be created over a file, so a file at the path is as taken as a directory
    /// would be - and is skipped rather than thrown on.
    /// </summary>
    [Fact]
    public void A_file_in_the_way_counts_as_taken()
    {
        File.WriteAllText(Path.Combine(_parent, "taken"), string.Empty);
        Queue<string> draws = new(["taken", "free"]);

        string directory = Scratch.CreateDirectory(_parent, draws.Dequeue);

        Assert.Equal(Path.Combine(_parent, "free"), directory);
        Assert.True(Directory.Exists(directory));
    }

    [Fact]
    public void A_parent_where_every_name_is_taken_is_refused_rather_than_searched_forever()
    {
        Directory.CreateDirectory(Path.Combine(_parent, "taken"));
        int draws = 0;

        IOException refusal = Assert.Throws<IOException>(
            () => Scratch.CreateDirectory(_parent, () => { draws++; return "taken"; }));

        Assert.Equal(Scratch.Attempts, draws);
        Assert.Contains(_parent, refusal.Message, StringComparison.Ordinal);
    }

    private string Created()
    {
        string directory = Scratch.CreateDirectory();
        _created.Add(directory);

        return directory;
    }
}
