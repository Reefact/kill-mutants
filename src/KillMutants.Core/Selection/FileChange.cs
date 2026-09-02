namespace KillMutants.Selection;

/// <summary>What happened to a file between the base revision and the tree about to be built.</summary>
/// <remarks>
/// Three kinds, not git's full alphabet. A rename is asked for as a deletion plus an addition -
/// <c>--no-renames</c> - because the two paths mean different things here: the old path may have
/// been a test file whose disappearance widens the selection, and reporting only the destination
/// would lose that. Copies, type changes and merge conflicts are all read as modifications, which
/// is the conservative reading of each.
/// </remarks>
internal enum ChangeKind
{
    /// <summary>The file does not exist at the base revision.</summary>
    Added,

    /// <summary>The file exists at both revisions and differs.</summary>
    Modified,

    /// <summary>The file exists at the base revision and not in the tree to be built.</summary>
    Deleted,
}

/// <summary>One file a change touched, with its absolute path.</summary>
/// <param name="Path">
/// The absolute path. For a deletion it names a file that is not there, which is the point: it is
/// how a test file that used to exist is still visible to the selection.
/// </param>
/// <param name="Kind">What happened to it.</param>
internal sealed record FileChange(string Path, ChangeKind Kind);
