using KillMutants.Filtering;
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
/// The untracked files a project names with <c>--ignore-untracked</c> are left out for the same
/// reason, one step further: they are what its own tests write beside their sources, which a run of
/// the series wrote itself on a tree that was clean when it began (DEC0025). Only untracked files,
/// and only under the directory the run was pointed at, since the patterns are written relative to
/// it; an edited tracked file is a change to the code whatever a pattern says.
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
        CancellationToken cancellationToken = default) =>
        (await ReadAsync(directory, leaveAlone, [], cancellationToken).ConfigureAwait(false))?.State;

    /// <summary>
    /// The state of the code, leaving out the untracked files that match
    /// <paramref name="ignoreUntracked"/> - or null when <paramref name="directory"/> is not inside a
    /// git working copy that can be read.
    /// </summary>
    /// <param name="directory">A directory inside the working copy.</param>
    /// <param name="leaveAlone">The tool's own files, whose presence or edits do not count.</param>
    /// <param name="ignoreUntracked">
    /// The patterns of DEC0025, already validated, relative to <paramref name="directory"/>; empty for
    /// none.
    /// </param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    public static async Task<CodeReading?> ReadAsync(
        string directory,
        IEnumerable<string> leaveAlone,
        IReadOnlyList<string> ignoreUntracked,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(leaveAlone);
        ArgumentNullException.ThrowIfNull(ignoreUntracked);

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

            // The commit alone does not say which directory of it was measured, and a series must
            // not continue from another one - see CodeState.
            string? prefix = await repository.PrefixOfAsync(directory, cancellationToken).ConfigureAwait(false);

            if (prefix is null)
            {
                return null;
            }

            // Git's prefix ends in '/', which the identity does without; the top is the empty string
            // either way.
            string named = prefix.TrimEnd('/');

            if (ignoreUntracked.Count == 0)
            {
                // The plain question, as every run that writes a report asks it: no file has to be
                // named, so an untracked directory is not listed file by file.
                bool dirty = await repository
                    .HasUncommittedChangesAsync(leaveAlone, cancellationToken)
                    .ConfigureAwait(false);

                return new CodeReading(new CodeState(head, !dirty, named), []);
            }

            WorkingTreeChanges changes = await repository.ChangesAsync(leaveAlone, cancellationToken).ConfigureAwait(false);
            (IReadOnlyList<string> counted, IReadOnlyList<IgnoredFile> ignored) =
                Split(changes.Untracked, prefix, ignoreUntracked, repository.Root);

            return new CodeReading(
                new CodeState(head, !changes.Tracked && counted.Count == 0, named),
                ignored);
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

    /// <summary>
    /// What makes the tree differ from its commit once <paramref name="leaveAlone"/> and the files
    /// <paramref name="ignoreUntracked"/> names are left out - for a refusal to say which files they
    /// are - or null when there is no working copy to read.
    /// </summary>
    /// <remarks>
    /// Never a reason to refuse anything: it is asked after a refusal has been decided, to word it.
    /// The paths are relative to the top of the working copy, as <c>git status</c> shows them, since
    /// the untracked files a person has to look at may sit outside the directory the run was
    /// pointed at.
    /// </remarks>
    public static async Task<WorkingTreeChanges?> DifferencesAsync(
        string directory,
        IEnumerable<string> leaveAlone,
        IReadOnlyList<string> ignoreUntracked,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(leaveAlone);
        ArgumentNullException.ThrowIfNull(ignoreUntracked);

        GitRepository? repository = await GitRepository
            .ContainingOrNullAsync(directory, cancellationToken)
            .ConfigureAwait(false);

        if (repository is null)
        {
            return null;
        }

        try
        {
            string? prefix = await repository.PrefixOfAsync(directory, cancellationToken).ConfigureAwait(false);

            if (prefix is null)
            {
                return null;
            }

            WorkingTreeChanges changes = await repository.ChangesAsync(leaveAlone, cancellationToken).ConfigureAwait(false);

            return changes with
            {
                Untracked = Split(changes.Untracked, prefix, ignoreUntracked, repository.Root).Counted,
            };
        }
        catch (Selection.ChangeSelectionException)
        {
            return null;
        }
    }

    /// <summary>
    /// Sorts the untracked files into those that still count and those a pattern ignores. A file
    /// outside the directory the run was pointed at cannot be named by a pattern written relative to
    /// it, so it always counts.
    /// </summary>
    private static (IReadOnlyList<string> Counted, IReadOnlyList<IgnoredFile> Ignored) Split(
        IReadOnlyList<string> untracked, string prefix, IReadOnlyList<string> ignoreUntracked, string root)
    {
        List<string> counted = [];
        List<IgnoredFile> ignored = [];

        foreach (string path in untracked)
        {
            if (ignoreUntracked.Count > 0 &&
                path.StartsWith(prefix, StringComparison.Ordinal) &&
                path[prefix.Length..] is { Length: > 0 } relative &&
                UntrackedPatterns.Matches(ignoreUntracked, relative))
            {
                ignored.Add(new IgnoredFile(relative, Path.GetFullPath(Path.Combine(root, path))));
            }
            else
            {
                counted.Add(path);
            }
        }

        return (counted, ignored);
    }
}

/// <summary>The state of the code, and the untracked files a series was told not to count.</summary>
/// <param name="State">The commit, whether the tree is exactly it once those files are left out, and the directory.</param>
/// <param name="Ignored">The untracked files a pattern of DEC0025 left out, in ordinal order.</param>
public sealed record CodeReading(CodeState State, IReadOnlyList<IgnoredFile> Ignored);
