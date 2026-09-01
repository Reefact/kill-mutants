using System.Globalization;
using KillMutants.Mutations;

namespace KillMutants.Reporting;

/// <summary>Something about a run that a reader must know before trusting its score.</summary>
/// <param name="Text">What to tell them.</param>
/// <remarks>
/// <para>
/// Not a status and not a finding: a statement about how far the number at the bottom can be
/// trusted. The case that earned it was measured on another tool - 4 575 mutants, 2 505 of them
/// counted as detected only because they timed out, no survivors at all, and a report that said
/// "100%". Half the component had never been judged, and nothing in the output invited anyone to
/// doubt it.
/// </para>
/// <para>
/// A tool that measures may under-detect. It may not mislead. Where the score rests on an
/// assumption rather than on a test failing, that has to be said out loud, in the report, next to
/// the number it qualifies.
/// </para>
/// </remarks>
public sealed record RunWarning(string Text)
{
    /// <summary>
    /// The share of detections that may be timeouts before the score is called into question.
    /// </summary>
    /// <remarks>
    /// A judgement, stated so a reader can disagree with it. One timeout in a thousand detections is
    /// noise; one in ten is the score resting on something no test asserted.
    /// </remarks>
    public const double TimeoutShareOfDetections = 0.1;

    /// <summary>Everything worth saying about <paramref name="report"/> before its score.</summary>
    internal static IReadOnlyList<RunWarning> For(MutationTestReport report)
    {
        List<RunWarning> warnings = [];

        if (report.Detected > 0 &&
            report.TimedOut >= report.Detected * TimeoutShareOfDetections &&
            report.TimedOut > 0)
        {
            warnings.Add(new RunWarning(
                $"{Share(report.TimedOut, report.Detected)} of the mutants counted as detected " +
                $"({Count(report.TimedOut)} of {Count(report.Detected)}) were counted so because " +
                "they timed out, not because a test failed. A timeout is read as a detection on the " +
                "assumption that the mutation changed behaviour enough to hang the suite; at this " +
                "proportion, that assumption is carrying the score. Each was re-run on its own " +
                "before being believed, so contention is not the cause - the time budget is."));
        }

        MutantResult[] overturned = [.. report.Results.Where(result => result.Overturned is not null)];

        if (overturned.Length > 0)
        {
            warnings.Add(new RunWarning(
                $"{Count(overturned.Length)} mutant(s) timed out while the workers were running and " +
                "did not when re-run on their own: " +
                string.Join(", ", overturned.Select(result => result.Mutant.Key)) +
                ". Their corrected verdicts are the ones reported. This says nothing about those " +
                "mutants and something about the run: the time budget is tight for this level of " +
                "concurrency, and a tool without that second pass would have counted every one of " +
                "them as detected."));
        }

        MutantResult[] disputed = [.. report.Results.Where(result => result.Disagreement is not null)];

        if (disputed.Length > 0)
        {
            warnings.Add(new RunWarning(
                $"{Count(disputed.Length)} verdict(s) did not survive being tested again: " +
                string.Join(", ", disputed.Select(result => result.Mutant.Key)) +
                ". A verdict that does not reproduce was never a measurement, and this tool cannot " +
                "tell which of the two runs was right. Every one of these is worth settling by " +
                "hand before the score is believed."));
        }

        if (report.Untestable > 0 && report.Total > 0)
        {
            warnings.Add(new RunWarning(
                $"{Count(report.Untestable)} of {Count(report.Total)} mutants could not be built " +
                "and are outside the score entirely. The score describes the rest."));
        }

        return warnings;
    }

    /// <inheritdoc />
    public override string ToString() => Text;

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Share(int part, int whole) =>
        (100d * part / whole).ToString("0.#", CultureInfo.InvariantCulture) + "%";
}
