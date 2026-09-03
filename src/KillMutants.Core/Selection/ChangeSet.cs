namespace KillMutants.Selection;

/// <summary>Everything a partial run knows about the change it was asked to judge.</summary>
/// <param name="ComparedFrom">
/// What the change is measured from, named by whatever supplied it. The core transcribes this into
/// the report and never reads a format out of it.
/// </param>
/// <param name="ComparedTo">The state that will be measured, named the same way.</param>
/// <param name="ComparedToIsExact">
/// False when what will be built is not exactly the state <paramref name="ComparedTo"/> names.
/// </param>
/// <param name="Changes">Every file the change touched.</param>
/// <remarks>
/// A source is free to measure against something other than the state it names, and one that does
/// says so here rather than letting the report imply otherwise - because what a run reports on has
/// to be what it built. Measuring one state while mutating another describes code that never
/// existed.
/// </remarks>
public sealed record ChangeSet(
    string ComparedFrom,
    string ComparedTo,
    bool ComparedToIsExact,
    IReadOnlyList<FileChange> Changes);
