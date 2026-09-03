namespace KillMutants.Reporting;

/// <summary>What population a run inspected, so its report can be interpreted.</summary>
/// <param name="ComparedFrom">
/// What the change was measured from, named by whatever supplied it, or null for a full run.
/// </param>
/// <param name="ComparedTo">The state that was measured, named the same way, or null.</param>
/// <param name="ComparedToIsExact">
/// False when what was measured is not exactly the state <paramref name="ComparedTo"/> names - code
/// that has been edited but not recorded anywhere. The run reports on what it built, and says so.
/// </param>
/// <param name="ChangedFiles">How many files the change touched.</param>
/// <remarks>
/// <para>
/// A partial report whose out-of-scope mutants are simply absent is indistinguishable from a full run
/// that happened to have that many mutants. A dashboard, or a reader six months later, cannot tell
/// which population was inspected or reproduce the selection - so the run mode and the two states
/// are recorded beside the environment and the time budgets, for the same reason those are: a report
/// that cannot be interpreted is not a report. See DEC0010.
/// </para>
/// <para>
/// The two states are opaque here. What names them - a commit, a build number, a label a continuous
/// integration job chose - is the business of whatever answered the question, and this record
/// transcribes it. That is why nothing below reads a length, a prefix or a format out of them.
/// </para>
/// <para>
/// This is metadata about the run, not a status a mutant can carry. DEC0010 refuses the latter: a
/// state meaning "outside the diff" is a way for the denominator to change without the label
/// changing, which is the seam that document is about.
/// </para>
/// </remarks>
public sealed record RunScope(
    string? ComparedFrom,
    string? ComparedTo,
    bool ComparedToIsExact,
    int ChangedFiles)
{
    /// <summary>How much of a state's name a console line carries before it is cut.</summary>
    /// <remarks>
    /// A display width, not a format. Names can be long and a progress line is one line; this makes
    /// no claim about what a name means or where it can be cut safely, which is why the report file
    /// keeps them whole.
    /// </remarks>
    private const int DisplayWidth = 12;

    /// <summary>A run over the whole codebase.</summary>
    public static RunScope WholeCodebase { get; } = new(null, null, true, 0);

    /// <summary>True when only what a change touched was inspected.</summary>
    public bool IsPartial => ComparedFrom is not null;

    /// <summary>The two states as a reader wants them: short, and honest about what was built.</summary>
    public override string ToString()
    {
        if (!IsPartial)
        {
            return "the whole codebase";
        }

        string measured = ComparedToIsExact
            ? Short(ComparedTo)
            : $"edited code based on {Short(ComparedTo)}";

        return $"changes from {Short(ComparedFrom)} to {measured}";
    }

    private static string Short(string? state) =>
        state is null ? "an unknown state" :
        state.Length > DisplayWidth ? state[..DisplayWidth] : state;
}
