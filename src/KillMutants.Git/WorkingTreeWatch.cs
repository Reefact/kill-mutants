namespace KillMutants.Git;

/// <summary>
/// Notices the files a run leaves in the working copy it measured: the untracked files present
/// after it and not before.
/// </summary>
/// <remarks>
/// <para>
/// A sandbox keeps a mutated assembly out of the build output, and nothing out of the source tree. A
/// test that writes next to its own source - a snapshot library such as Verify writes a
/// <c>.received</c> file there when a snapshot does not match, located through
/// <c>[CallerFilePath]</c> - writes into the developer's tree whenever a mutant breaks it. On
/// <c>Reefact/first-class-errors</c> two such files were found after the run, looking like a failed
/// snapshot of the developer's own (#51).
/// </para>
/// <para>
/// Reported, not prevented: what a test writes and where is the test's business, and isolating the
/// whole tree is RB-023's open question. Telling the person which files came from the run is the
/// part that can be done for every suite, and it keeps them out of a careless commit.
/// </para>
/// </remarks>
public sealed class WorkingTreeWatch
{
    private readonly GitRepository _repository;
    private readonly HashSet<string> _before;

    private WorkingTreeWatch(GitRepository repository, IEnumerable<string> before)
    {
        _repository = repository;
        _before = new HashSet<string>(before, StringComparer.Ordinal);
    }

    /// <summary>
    /// Reads the untracked files of the working copy holding <paramref name="directory"/>, or returns
    /// null when it is not in one git can read - an ordinary place to run, with nothing to watch.
    /// </summary>
    public static async Task<WorkingTreeWatch?> StartAsync(string directory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        GitRepository? repository = await GitRepository
            .ContainingOrNullAsync(directory, cancellationToken)
            .ConfigureAwait(false);

        if (repository is null)
        {
            return null;
        }

        try
        {
            return new WorkingTreeWatch(
                repository,
                await repository.UntrackedFilesAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (Selection.ChangeSelectionException)
        {
            return null;
        }
    }

    /// <summary>The top of the working copy, which the paths returned are relative to.</summary>
    public string Root => _repository.Root;

    /// <summary>
    /// The untracked files that appeared since the watch started, relative to <see cref="Root"/>,
    /// leaving out <paramref name="leaveAlone"/> - the tool's own report files - and in ordinal order.
    /// </summary>
    public async Task<IReadOnlyList<string>> NewFilesAsync(
        IEnumerable<string> leaveAlone,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(leaveAlone);

        // Placed in the working copy by git rather than by comparing paths: the root git reports is
        // the real one, and a report named through a symbolic link - macOS keeps its temporary
        // directory behind one - would otherwise not match and be reported as left behind.
        HashSet<string> own = new(StringComparer.Ordinal);

        foreach (string path in leaveAlone.Select(Path.GetFullPath))
        {
            if (Path.GetDirectoryName(path) is { } directory &&
                Directory.Exists(directory) &&
                await _repository.PrefixOfAsync(directory, cancellationToken).ConfigureAwait(false) is { } prefix)
            {
                own.Add(prefix + Path.GetFileName(path));
            }
        }

        IReadOnlyList<string> now = await _repository.UntrackedFilesAsync(cancellationToken).ConfigureAwait(false);

        return
        [
            .. now
                .Where(path => !_before.Contains(path) && !own.Contains(path))
                .Order(StringComparer.Ordinal),
        ];
    }
}
