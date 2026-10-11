using KillMutants.Reporting;

namespace KillMutants.Git;

/// <summary>Reads the state of the code a run is about to measure, from git.</summary>
/// <remarks>
/// <para>
/// The same two questions a partial run already asks - what commit <c>HEAD</c> points at, and whether
/// the tree is exactly that commit - asked for a different reason: a run that continues a series
/// (DEC0015) has to know that the code it measures is the code the series measured, and the commit
/// with a clean tree is the one identity this repository already stands behind.
/// </para>
/// <para>
/// The files KillMutants writes beside its report are left out of the dirtiness read. They sit in the
/// working copy before the run, untracked, and a CI job restores them there; counted as edits, they
/// would make every series unable to continue in the one deployment it exists for.
/// </para>
/// <para>
/// The directory the run was pointed at is read as git names it, relative to the top of the working
/// copy, so that two checkouts of one commit agree on it and two directories of one commit do not.
/// </para>
/// </remarks>
public static class GitCodeState
{
    /// <summary>
    /// The commit <c>HEAD</c> names, whether the tree is exactly it, and where in it
    /// <paramref name="directory"/> sits - or null when it is not inside a git working copy that can
    /// be read.
    /// </summary>
    /// <param name="directory">A directory inside the working copy.</param>
    /// <param name="leaveAlone">
    /// Files whose presence or edits do not count - the tool's own, written beside the report.
    /// </param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    public static async Task<CodeState?> ReadAsync(
        string directory,
        IEnumerable<string> leaveAlone,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(leaveAlone);

        GitRepository? repository = await GitRepository
            .ContainingOrNullAsync(directory, cancellationToken)
            .ConfigureAwait(false);

        if (repository is null)
        {
            return null;
        }

        try
        {
            string head = await repository.ResolveAsync("HEAD", cancellationToken).ConfigureAwait(false);

            bool dirty = await repository
                .HasUncommittedChangesAsync(leaveAlone, cancellationToken)
                .ConfigureAwait(false);

            // The commit alone does not say which directory of it was measured, and a series must
            // not continue from another one - see CodeState. Git's prefix ends in '/', which the
            // identity does without; the top is the empty string either way.
            string? prefix = await repository.PrefixOfAsync(directory, cancellationToken).ConfigureAwait(false);

            if (prefix is null)
            {
                return null;
            }

            return new CodeState(head, !dirty, prefix.TrimEnd('/'));
        }
        catch (Selection.ChangeSelectionException)
        {
            // A working copy with no commit yet, or one git cannot describe. For a run that only
            // records its state that is the same answer as no working copy at all; for a run asked
            // to continue a series, the caller refuses with a message that says what a series needs.
            // The exception's own message is a partial run's, about --since, and would mislead here.
            return null;
        }
    }
}
