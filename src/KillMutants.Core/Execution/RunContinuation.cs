using KillMutants.Reporting;

namespace KillMutants.Execution;

/// <summary>
/// What a caller that keeps a plan beside the report hands the run: the series it belongs to, if
/// any, what identifies it, the earlier state it may continue, and where the plan goes.
/// </summary>
/// <param name="Series">The name the caller gave the series, or null for a run that named none.</param>
/// <param name="Identity">
/// What is known of this run's identity before it starts: the code state, the tool, the settings.
/// The run completes it once it has selected its mutants.
/// </param>
/// <param name="Earlier">
/// The plan this run continues, or null when it starts a series afresh - or none at all.
/// </param>
/// <param name="EarlierVerdicts">The verdicts the log beside that plan holds; empty when it starts afresh.</param>
/// <param name="Store">Where the plan is written.</param>
/// <param name="IgnoreUntracked">
/// The patterns this run was given with <c>--ignore-untracked</c>, or null for none (DEC0025).
/// </param>
/// <param name="IgnoredUntracked">
/// The untracked files those patterns left out of the code state, or null for none. The run refuses
/// to go on if the compiler reads one of them, and records them in the plan and the report.
/// </param>
/// <remarks>
/// The caller decides whether the series continues, because the caller is the one that opened the
/// log - continued or emptied - before the run began, and the two decisions have to be the same one.
/// What the run adds is the half of the identity only it can know, compared at the moment it
/// selects its mutants. See DEC0015 and DEC0016.
/// </remarks>
public sealed record RunContinuation(
    string? Series,
    RunIdentity Identity,
    RunPlan? Earlier,
    IReadOnlyList<LoggedVerdict> EarlierVerdicts,
    RunPlanStore Store,
    IReadOnlyList<string>? IgnoreUntracked = null,
    IReadOnlyList<IgnoredFile>? IgnoredUntracked = null);
