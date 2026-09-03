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

/// <summary>One file a change touched, with its absolute path.</summary>
/// <param name="Path">
/// The absolute path. For a deletion it names a file that is not there, which is the point: it is
/// how a test file that used to exist is still visible to the selection.
/// </param>
/// <param name="Kind">What happened to it.</param>
public sealed record FileChange(string Path, ChangeKind Kind);
