namespace KillMutants.Selection;

/// <summary>Everything a partial run knows about the change it was asked to judge.</summary>
/// <param name="BaseRevision">The resolved commit the change is measured from.</param>
/// <param name="HeadRevision">The resolved commit the working tree is checked out at.</param>
/// <param name="WorkingTreeDiffers">
/// True when the tree that will be built is not exactly <paramref name="HeadRevision"/>.
/// </param>
/// <param name="Changes">Every file the change touched.</param>
/// <remarks>
/// The diff is taken against the working tree rather than against <paramref name="HeadRevision"/>,
/// because the working tree is what gets built and mutated. On a clean checkout - every CI job -
/// the two are the same thing. On a laptop with uncommitted work they are not, and measuring the
/// commit while mutating the working tree would report on code that was never built. The report
/// says which of the two it was, so a reader can tell a reproducible run from a local one.
/// </remarks>
internal sealed record ChangeSet(
    string BaseRevision,
    string HeadRevision,
    bool WorkingTreeDiffers,
    IReadOnlyList<FileChange> Changes);
