namespace KillMutants.Reporting;

/// <summary>That a run was stopped before every mutant it selected had a verdict.</summary>
/// <param name="Selected">
/// How many mutants the run had selected, or null when it was stopped before it got as far as
/// generating them.
/// </param>
/// <remarks>
/// Why it was stopped is not recorded here. The caller asked for the stop - a deadline it set, a
/// signal it received - and is the one that can say which; this assembly only knows that it was
/// asked. See DEC0013.
/// </remarks>
public sealed record EarlyStop(int? Selected);
