namespace KillMutants.Git;

/// <summary>How a working tree differs from the commit <c>HEAD</c> points at.</summary>
/// <param name="Tracked">True when a tracked file or a submodule differs.</param>
/// <param name="Untracked">
/// Every untracked file git does not ignore, relative to the top of the working copy, with <c>/</c>,
/// in ordinal order.
/// </param>
public sealed record WorkingTreeChanges(bool Tracked, IReadOnlyList<string> Untracked);
