using System.Globalization;
using System.Runtime.InteropServices;

namespace KillMutants.Reporting;

/// <summary>What the run ran on, and under what limits.</summary>
/// <param name="Runtime">The .NET runtime that hosted KillMutants.</param>
/// <param name="OperatingSystem">The operating system it ran on.</param>
/// <param name="ProcessorCount">Logical processors the machine offered.</param>
/// <param name="WorkerCount">How many mutants were tested at once.</param>
/// <param name="TestFramework">The xUnit the test applications ran on, or null when unknown.</param>
/// <param name="TimeoutBudgets">The per-mutant time budget, and what it was derived from, one entry per project mutated.</param>
/// <param name="KillsReVerified">How many mutants reported killed were tested a second time, alone.</param>
/// <remarks>
/// <para>
/// Two reports without this are not comparable, and nobody notices. The case that earned it: the
/// same commit measured on a CI runner and in a container disagreed on seventeen mutants, and the
/// first day of the investigation went on establishing what the two runs had actually differed in -
/// SDK, concurrency, cores - none of which either report stated.
/// </para>
/// <para>
/// The time budget is here for a sharper reason. A mutant reported as timed out is unexplainable
/// after the fact without the limit it exceeded, and a run that calibrates that limit badly can
/// report most of a component as detected without a single test having failed. Publishing the budget
/// is what lets a reader check that story rather than take it.
/// </para>
/// </remarks>
public sealed record RunEnvironment(
    string Runtime,
    string OperatingSystem,
    int ProcessorCount,
    int WorkerCount,
    string? TestFramework,
    IReadOnlyList<TimeBudget> TimeoutBudgets,
    int KillsReVerified)
{
    /// <summary>Reads what can be read from the machine, given what the run decided.</summary>
    internal static RunEnvironment Describe(
        int workerCount,
        string? testFramework,
        IReadOnlyList<TimeBudget> timeoutBudgets,
        int killsReVerified) =>
        new(
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            Environment.ProcessorCount,
            workerCount,
            testFramework,
            timeoutBudgets,
            killsReVerified);

    /// <summary>The budgets as they are written in a report.</summary>
    public IReadOnlyList<double> TimeoutBudgetsInSeconds =>
        [.. TimeoutBudgets.Select(budget => Math.Round(budget.Budget.TotalSeconds, 2))];

    /// <summary>
    /// The baselines the budgets rest on, each measured with nothing else of ours running.
    /// </summary>
    /// <remarks>
    /// The denominator of the whole calculation, and the one number a reader needs to judge whether
    /// a timeout was the mutant's doing. It is measured alone and then spent under concurrency,
    /// which is exactly why it has to be said rather than assumed.
    /// </remarks>
    public IReadOnlyList<double> BaselineSecondsAlone =>
        [.. TimeoutBudgets.Select(budget => Math.Round(budget.Baseline.TotalSeconds, 2))];

    /// <summary>Renders as one line, for the console report.</summary>
    public override string ToString()
    {
        var culture = CultureInfo.InvariantCulture;
        string budgets = TimeoutBudgets.Count == 0
            ? "none"
            : string.Join(", ", TimeoutBudgets.Select(budget => budget.ToString()));

        // The sample size is here rather than left implicit because the check is a sample: a run
        // that verified none of its kills and a run that verified all of them both report "no
        // disagreements", and only one of those means anything.
        string verified = KillsReVerified == 0
            ? "no kills re-verified"
            : $"{KillsReVerified.ToString(culture)} kill(s) re-verified alone";

        return
            $"{Runtime} on {OperatingSystem}, " +
            $"{WorkerCount.ToString(culture)} of {ProcessorCount.ToString(culture)} processors, " +
            $"{TestFramework ?? "test framework unknown"}, timeout budget {budgets}, {verified}";
    }
}
