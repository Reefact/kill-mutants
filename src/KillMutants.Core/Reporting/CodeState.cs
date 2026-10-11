namespace KillMutants.Reporting;

/// <summary>The state of the code a run measured, as whatever read the working copy names it.</summary>
/// <param name="Name">
/// What names the state - a commit, typically - as the reader of the working copy resolved it.
/// </param>
/// <param name="IsExact">
/// False when the tree that was built is not exactly the state <paramref name="Name"/> points at:
/// an edited or untracked file, a submodule at another revision.
/// </param>
/// <param name="Directory">
/// Where in that state the run was pointed, relative to the top of the working copy and written
/// with <c>/</c>; empty when it was pointed at the top.
/// </param>
/// <remarks>
/// <para>
/// Opaque here, as <see cref="RunScope.ComparedTo"/> already is: what produced the name is the
/// business of whatever read the working copy, and the core only ever compares two of them for
/// equality. See DEC0015.
/// </para>
/// <para>
/// The directory is part of the state because the commit alone does not say what was measured. A
/// mutant's key and the project graph are relative to the directory the run was pointed at, so two
/// services of one repository with the same layout and the same mutation sites - and different
/// tests - describe themselves identically from the commit down; a series started on one and
/// continued on the other would inherit the first's verdicts without running the second's tests.
/// Relative to the top rather than absolute, so that two checkouts of one commit agree on it.
/// </para>
/// </remarks>
public sealed record CodeState(string Name, bool IsExact, string Directory = "");
