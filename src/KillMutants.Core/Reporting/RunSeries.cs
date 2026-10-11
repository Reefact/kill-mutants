namespace KillMutants.Reporting;

/// <summary>That a report is the sum of several runs, and which.</summary>
/// <param name="Id">The name the caller gave the series.</param>
/// <param name="Inherited">How many verdicts this run took from earlier runs rather than reaching.</param>
/// <param name="Runs">Every run of the series, oldest first, the one that wrote this report last.</param>
/// <remarks>
/// Published beside the verdict for the reason <see cref="MutationTestReport.Stopped"/> is: a run that
/// inherited everything builds no sandbox and writes a complete report in minutes, and a reader has
/// to know that its counts are the sum of several runs before reading them as one. The flat
/// environment fields keep describing the run that wrote the report; what the others ran on is here.
/// See DEC0015.
/// </remarks>
public sealed record RunSeries(string Id, int Inherited, IReadOnlyList<PlannedRun> Runs);
