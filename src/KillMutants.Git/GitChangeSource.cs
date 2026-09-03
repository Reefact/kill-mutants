using KillMutants.Selection;

namespace KillMutants.Git;

/// <summary>Answers what a change touched, and what the code was before it, from git.</summary>
/// <remarks>
/// <para>
/// One of the possible answers to <see cref="IChangeSource"/>, and the only one shipped today.
/// Everything git-shaped lives on this side of the boundary: resolving a name to a commit, finding
/// the point two histories last agreed on, reading a diff, laying an old state back out on disk.
/// The core receives a list of paths and a directory.
/// </para>
/// <para>
/// The change is measured to the <em>working tree</em> rather than to the head commit, because the
/// working tree is what gets built and mutated. On a clean checkout - every CI job - they are the
/// same thing. On a laptop with uncommitted work they are not, and measuring the commit while
/// mutating the tree would report on code that was never built.
/// </para>
/// </remarks>
public sealed class GitChangeSource : IChangeSource
{
    private readonly GitRepository _repository;
    private readonly string _since;
    private string? _before;

    private GitChangeSource(GitRepository repository, string since)
    {
        _repository = repository;
        _since = since;
    }

    /// <inheritdoc />
    public string Root => _repository.Root;

    /// <summary>Finds the repository holding a directory, and reads changes since a revision.</summary>
    /// <param name="searchDirectory">A directory inside the repository.</param>
    /// <param name="since">The revision to measure the change from.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    public static async Task<IChangeSource> ForAsync(
        string searchDirectory,
        string since,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(since);

        GitRepository repository = await GitRepository
            .ContainingAsync(searchDirectory, cancellationToken)
            .ConfigureAwait(false);

        return new GitChangeSource(repository, since);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The comparison starts where the two histories last agreed, not at the named revision itself:
    /// a branch that has fallen behind its target would otherwise be judged on everything that
    /// landed on the target meanwhile, which its author did not write and cannot answer for.
    /// </remarks>
    public async Task<ChangeSet> ChangesAsync(CancellationToken cancellationToken = default)
    {
        string head = await _repository.ResolveAsync("HEAD", cancellationToken).ConfigureAwait(false);
        string named = await _repository.ResolveAsync(_since, cancellationToken).ConfigureAwait(false);

        _before = await _repository.MergeBaseAsync(named, head, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<FileChange> changes = await _repository
            .ChangesSinceAsync(_before, cancellationToken)
            .ConfigureAwait(false);

        bool dirty = await _repository
            .HasUncommittedChangesAsync(cancellationToken)
            .ConfigureAwait(false);

        return new ChangeSet(_before, head, dirty, changes);
    }

    /// <inheritdoc />
    public Task<ICodeSnapshot> OpenCodeBeforeAsync(CancellationToken cancellationToken = default)
    {
        if (_before is null)
        {
            throw new InvalidOperationException(
                "The changes have to be read before the code they were measured against can be.");
        }

        return _repository.OpenCodeBeforeAsync(_before, cancellationToken);
    }
}
