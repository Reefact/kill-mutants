namespace KillMutants.Reporting;

/// <summary>What population a run inspected, so its report can be interpreted.</summary>
/// <param name="BaseRevision">The commit a partial run measured from, or null for a full run.</param>
/// <param name="HeadRevision">The commit the working tree was checked out at, or null.</param>
/// <param name="WorkingTreeDiffers">True when the tree that was built is not exactly the head commit.</param>
/// <param name="ChangedFiles">How many files the change touched.</param>
/// <remarks>
/// <para>
/// A partial report whose out-of-scope mutants are simply absent is indistinguishable from a full run
/// that happened to have that many mutants. A dashboard, or a reader six months later, cannot tell
/// which population was inspected or reproduce the selection - so the run mode and the resolved
/// revisions are recorded beside the environment and the time budgets, for the same reason those are:
/// a report that cannot be interpreted is not a report. See ADR-0010.
/// </para>
/// <para>
/// This is metadata about the run, not a status a mutant can carry. ADR-0010 refuses the latter: a
/// state meaning "outside the diff" is a way for the denominator to change without the label
/// changing, which is the seam that document is about.
/// </para>
/// </remarks>
public sealed record RunScope(
    string? BaseRevision,
    string? HeadRevision,
    bool WorkingTreeDiffers,
    int ChangedFiles)
{
    /// <summary>A run over the whole codebase.</summary>
    public static RunScope WholeCodebase { get; } = new(null, null, false, 0);

    /// <summary>True when only what a change touched was inspected.</summary>
    public bool IsPartial => BaseRevision is not null;

    /// <summary>The revisions as a reader wants them: short, and honest about the working tree.</summary>
    public override string ToString()
    {
        if (!IsPartial)
        {
            return "the whole codebase";
        }

        string head = WorkingTreeDiffers
            ? $"the working tree at {Short(HeadRevision)}"
            : Short(HeadRevision);

        return $"changes from {Short(BaseRevision)} to {head}";
    }

    private static string Short(string? revision) =>
        revision is null ? "an unknown revision" :
        revision.Length > 8 ? revision[..8] : revision;
}
