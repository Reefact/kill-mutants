namespace KillMutants.Selection;

/// <summary>What happened to a file between the earlier state and the code about to be built.</summary>
/// <remarks>
/// Three kinds, and a source is expected to reduce whatever vocabulary it has to these. A rename
/// is two of them - a deletion and an addition - because the two paths mean different things here:
/// the old path may have been a test file whose disappearance widens the selection, and reporting
/// only the destination would lose that. Anything a source cannot classify belongs in
/// <see cref="ChangeKind.Modified"/>, which is the conservative reading.
/// </remarks>
public enum ChangeKind
{
    /// <summary>The file does not exist in the earlier state.</summary>
    Added,

    /// <summary>The file exists in both states and differs.</summary>
    Modified,

    /// <summary>The file exists in the earlier state and not in the code to be built.</summary>
    Deleted,
}

/// <summary>One thing a change touched, with its absolute path.</summary>
/// <param name="Path">
/// The absolute path. For a deletion it names a file that is not there, which is the point: it is
/// how a test file that used to exist is still visible to the selection.
/// </param>
/// <param name="Kind">What happened to it.</param>
/// <param name="IsWholeComponent">
/// True when the path names a whole subtree the source tracks as one unit rather than a single
/// file, so that everything beneath it has to be taken as changed.
/// </param>
/// <remarks>
/// The component flag exists because the core was inferring it, and review found what that cost. A
/// source that can only name a subtree - git reports a submodule as one entry, its own path and
/// nothing beneath it - has to be able to say so, and only the source knows. The core used to ask
/// the filesystem whether the path was a directory, which answers about the code as it is now: a
/// component the change <em>removed</em> is no longer on disk, so the probe said "file", the path
/// matched no project, and the run passed over a subtree that had gone.
/// </remarks>
public sealed record FileChange(string Path, ChangeKind Kind, bool IsWholeComponent = false);
