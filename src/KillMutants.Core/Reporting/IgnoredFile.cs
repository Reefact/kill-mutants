namespace KillMutants.Reporting;

/// <summary>An untracked file a series was told not to count when it read its tree. See DEC0025.</summary>
/// <param name="Path">
/// Relative to the directory the run was pointed at, with <c>/</c>, as the pattern matched it - which
/// is how the plan and the report name it.
/// </param>
/// <param name="FullPath">
/// Where it is on this machine, for comparing with the files the compiler reads; never written down.
/// </param>
public sealed record IgnoredFile(string Path, string FullPath);
