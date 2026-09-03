namespace KillMutants.Selection;

/// <summary>The code as it was before the change, laid out on disk for reading.</summary>
/// <remarks>
/// <para>
/// A directory and nothing else. What produced it - a version control system, a copy, a download -
/// is none of the core's business: the selection reads project files and evaluates them, and both
/// work the same whatever put the files there.
/// </para>
/// <para>
/// Not called a baseline. In this codebase a baseline is the unmutated compilation a run verifies
/// before it starts, and one word for two things in the same domain is how a reader comes to trust
/// the wrong one.
/// </para>
/// <para>
/// Disposable because producing one can leave state outside the directory itself, which deleting
/// the directory would not undo.
/// </para>
/// </remarks>
public interface ICodeSnapshot : IDisposable
{
    /// <summary>The directory the code was laid out in.</summary>
    string Root { get; }

    /// <summary>
    /// What the snapshot could not reproduce, empty when it is complete.
    /// </summary>
    /// <remarks>
    /// A snapshot is allowed to be incomplete - a component whose contents live elsewhere may not be
    /// available offline - and is never allowed to be quietly incomplete. What it could not lay out
    /// is named here, so the run can say what it did not check rather than reporting on a comparison
    /// it did not make.
    /// </remarks>
    IReadOnlyList<string> Missing { get; }
}
