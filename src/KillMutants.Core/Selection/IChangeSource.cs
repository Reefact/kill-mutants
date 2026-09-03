namespace KillMutants.Selection;

/// <summary>Where a partial run learns what changed, and what the code was before.</summary>
/// <remarks>
/// <para>
/// The core declares this and does not implement it. Everything a partial run needs from the outside
/// is here: which files a change touched, and the code as it stood before that change. Both are
/// facts about code, and neither names a version control system - so the thing that answers them
/// can be git, a continuous integration job that already knows its own diff, or a test handing over
/// two directories.
/// </para>
/// <para>
/// The dependency runs one way on purpose. An implementation references the core; the core
/// references no implementation and cannot call one even by accident. That is what keeps the
/// selection - which projects consume a file, which suites a change touches, what stopped being
/// covered - expressed in the language of code rather than of commits.
/// </para>
/// </remarks>
public interface IChangeSource
{
    /// <summary>The directory the compared code is rooted at.</summary>
    string Root { get; }

    /// <summary>What the change touched, and what to call the two states in a report.</summary>
    Task<ChangeSet> ChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Lays the code as it was before the change out for reading.</summary>
    Task<ICodeSnapshot> OpenCodeBeforeAsync(CancellationToken cancellationToken = default);
}
