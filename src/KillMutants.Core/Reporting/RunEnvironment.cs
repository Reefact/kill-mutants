using System.Globalization;
using System.Runtime.InteropServices;

namespace KillMutants.Reporting;

/// <summary>What the run ran on, and under what limits.</summary>
/// <param name="Runtime">The .NET runtime that hosted KillMutants.</param>
/// <param name="OperatingSystem">The operating system it ran on.</param>
/// <param name="ProcessorCount">Logical processors the machine offered.</param>
/// <param name="WorkerCount">How many mutants were tested at once.</param>
/// <param name="TestFramework">The xUnit the test applications ran on, or null when unknown.</param>
/// <param name="TimeoutBudgets">The per-mutant time budget allowed, one entry per project mutated.</param>
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
    IReadOnlyList<TimeSpan> TimeoutBudgets)
{
    /// <summary>Reads what can be read from the machine, given what the run decided.</summary>
    internal static RunEnvironment Describe(
        int workerCount,
        string? testFramework,
        IReadOnlyList<TimeSpan> timeoutBudgets) =>
        new(
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            Environment.ProcessorCount,
            workerCount,
            testFramework,
            timeoutBudgets);

    /// <summary>The budgets as they are written in a report.</summary>
    public IReadOnlyList<double> TimeoutBudgetsInSeconds =>
        [.. TimeoutBudgets.Select(budget => Math.Round(budget.TotalSeconds, 2))];

    /// <summary>Renders as one line, for the console report.</summary>
    public override string ToString()
    {
        var culture = CultureInfo.InvariantCulture;
        string budgets = TimeoutBudgets.Count == 0
            ? "none"
            : string.Join(
                ", ",
                TimeoutBudgets.Select(budget =>
                    budget.TotalSeconds.ToString("0.#", culture) + " s"));

        return
            $"{Runtime} on {OperatingSystem}, " +
            $"{WorkerCount.ToString(culture)} of {ProcessorCount.ToString(culture)} processors, " +
            $"{TestFramework ?? "test framework unknown"}, timeout budget {budgets}";
    }
}
