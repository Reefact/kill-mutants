namespace KillMutants.Reporting;

/// <summary>The state of the code a run measured, as whatever read the working copy names it.</summary>
/// <param name="Name">
/// What names the state - a commit, typically - as the reader of the working copy resolved it.
/// </param>
/// <param name="IsExact">
/// False when the tree that was built is not exactly the state <paramref name="Name"/> points at:
/// an edited or untracked file, a submodule at another revision.
/// </param>
/// <remarks>
/// Opaque here, as <see cref="RunScope.ComparedTo"/> already is: what produced the name is the
/// business of whatever read the working copy, and the core only ever compares two of them for
/// equality. See DEC0015.
/// </remarks>
public sealed record CodeState(string Name, bool IsExact);
