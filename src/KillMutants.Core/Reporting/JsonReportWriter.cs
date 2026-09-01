using System.Text.Encodings.Web;
using System.Text.Json;
using KillMutants.Mutations;

namespace KillMutants.Reporting;

/// <summary>Writes a run's outcome as JSON, for anything that is not a person.</summary>
/// <remarks>
/// Deliberately flat and boring. This is what a CI job, a dashboard or a diff between two runs will
/// read, so it names every mutant with a stable identity and its exact position rather than
/// summarising. The console report is the one allowed to be selective.
/// </remarks>
public static class JsonReportWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,

        // The default encoder escapes < > & as \u003C and friends. In this report those characters
        // are the payload - the mutated operators themselves - so escaping them would make the file
        // unreadable and, worse, unsearchable: grepping a report for ">=" would find nothing. The
        // encoder is not a security boundary here; a consumer embedding this JSON in HTML must
        // encode it there, as it must for any data it did not author.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Writes the report as JSON.</summary>
    public static void Write(TextWriter writer, MutationTestReport report)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(report);

        writer.Write(JsonSerializer.Serialize(Describe(report), Options));
    }

    private static object Describe(MutationTestReport report) => new
    {
        tool = "KillMutants",
        // Null rather than NaN when nothing could be tested: JSON has no NaN, and a consumer that
        // sees null knows the score is undefined instead of reading a zero that was never measured.
        score = report.Score.IsUndefined ? null : (double?)Math.Round(report.Score.Value, 4),
        scoreDisplay = report.Score.ToString(),
        durationSeconds = Math.Round(report.Duration.TotalSeconds, 2),
        totals = new
        {
            mutants = report.Total,
            killed = report.Killed,
            survived = report.Survived,
            timedOut = report.TimedOut,
            compileErrors = report.CompileErrors,
            noCoverage = report.Uncovered,

            // The three that define the score, so a consumer never has to re-derive which status
            // counts as what: score = detected / (detected + undetected).
            detected = report.Detected,
            undetected = report.Undetected,
            untestable = report.Untestable,
        },
        byMutator = report.ByMutator.Select(Describe).ToArray(),
        mutants = report.Results.Select(Describe).ToArray(),
    };

    private static object Describe(MutatorSummary family) => new
    {
        mutator = family.Mutator.ToString(),
        mutants = family.Total,
        detected = family.Detected,
        undetected = family.Undetected,
        untestable = family.Untestable,
        score = family.Score.IsUndefined ? null : (double?)Math.Round(family.Score.Value, 4),
    };

    private static object Describe(MutantResult result) => new
    {
        // The stable one, first, because it is the one to join two reports on. `id` is a counter
        // and means nothing outside the run that produced it.
        key = result.Mutant.Key.ToString(),
        id = result.Mutant.Id.ToString(),
        mutator = result.Mutant.Mutator.ToString(),
        status = result.Status.ToString(),
        outcome = result.Outcome.ToString(),
        path = result.Mutant.RelativePath,
        file = result.Mutant.Location.FilePath,
        line = result.Mutant.Location.Line,
        character = result.Mutant.Location.Character,
        original = result.Mutant.OriginalText,
        mutated = result.Mutant.MutatedText,

        // With the three fields above and these names, a kill can be reproduced by hand: put the
        // mutation in the file at that position, run these tests, watch them fail. A verdict nobody
        // can reproduce is a verdict nobody can dispute.
        killedBy = result.KilledBy.Select(test => test.ToString()).ToArray(),
        detail = result.Detail,
    };
}
