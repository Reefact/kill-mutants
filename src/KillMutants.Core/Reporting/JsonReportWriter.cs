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

    /// <summary>The same rules on one line, which is what a line of a JSON Lines file has to be.</summary>
    private static readonly JsonSerializerOptions LineOptions = new(Options) { WriteIndented = false };

    /// <summary>Writes the report as JSON.</summary>
    public static void Write(TextWriter writer, MutationTestReport report)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(report);

        writer.Write(JsonSerializer.Serialize(Describe(report), Options));
    }

    /// <summary>
    /// One mutant's verdict as a single line of JSON, in the shape every entry of the report's
    /// <c>mutants</c> has.
    /// </summary>
    /// <remarks>
    /// The same shape on purpose, and from the same code: a consumer that reads the verdict log a run
    /// left behind it, and later the report that run wrote, should not have to learn two ways of
    /// naming one mutant. See <see cref="VerdictLog"/>.
    /// </remarks>
    public static string Line(MutantResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return JsonSerializer.Serialize(Describe(result), LineOptions);
    }

    private static object Describe(MutationTestReport report) => new
    {
        tool = "KillMutants",

        // Null in two different situations, both of which mean "there is no number here": nothing
        // could be tested, or this was a partial run, which has no score by design. A consumer can
        // tell them apart from `scope`, and neither can be mistaken for a zero that was measured.
        // Emitting the ratio for a partial run would put a percentage over a population defined by
        // the diff into a field named like the one over a codebase - the exact seam DEC0010 refuses.
        // A stopped run has none either: the mutants it judged are the ones it reached first, not a
        // sample of anything (DEC0013).
        score = HasNoScore(report) || report.Score.IsUndefined
            ? null
            : (double?)Math.Round(report.Score.Value, 4),
        scoreDisplay = HasNoScore(report) ? null : report.Score.ToString(),
        durationSeconds = Math.Round(report.Duration.TotalSeconds, 2),

        // Which population was inspected. Without it, a partial report is indistinguishable from a
        // full run that happened to have that many mutants, and its selection cannot be reproduced.
        scope = Describe(report.Scope),

        // Two reports without this are not comparable, and nobody notices.
        environment = report.Environment is null ? null : Describe(report.Environment),
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

            // Outside the score by the project's own choice, not the tool's limitation, so it is
            // neither untestable nor undetected: its own count, beside the ones that define the
            // score (DEC0026).
            ignored = report.Ignored,
        },

        // Every rule the project wrote, with every mutant it silences - one two rules silence counted
        // under each - a rule that silenced nothing included: the choice that moves the score is
        // published next to the score it moved.
        ignoredCalls = report.IgnoredByRule.Select(rule => new
        {
            rule = rule.Rule,
            mutants = rule.Mutants,
        }).ToArray(),
        // The binary answer a partial run gives in place of a percentage, published so a CI job can
        // read it without re-deriving it from the counts. Both are meaningful on a full run too.
        hasUndetected = report.HasUndetected,
        inconclusive = report.IsInconclusive,

        // Whether the run judged everything it set out to, published beside the verdict for the
        // reason comparisonIsIncomplete is: on a stopped run hasUndetected false means "none found
        // among those judged", and a consumer that reads it without reading this acts on a run that
        // never got to the rest. A run that set something aside (DEC0022) is in the same place,
        // stopped or not. mutantsSelected is null when the run stopped before it had selected
        // anything, and counts the selected mutants a set-aside target left unjudged too.
        complete = report.IsComplete && report.MeasuredEverything,
        mutantsSelected = report.Selected,

        // What the run could not measure and why (DEC0022). Empty when it measured everything; when
        // it is not, no gate passes and the exit code is 2, and `score` covers the measured part
        // only - or is null when a project set aside at discovery leaves the population unknown.
        setAside = report.SetAside.Select(part => new
        {
            stage = part.Stage.ToString(),
            name = part.Name,
            reason = part.Reason,
        }).ToArray(),

        // Null for a run that named no series. Otherwise the sum this report is: how many verdicts
        // came from earlier runs, and what each run of the series ran on - the flat environment
        // below describes only the run that wrote this file (DEC0015).
        series = report.Series is null ? null : Describe(report.Series),

        // Projects the change stopped covering. They carry no mutants - nothing reaches them - so a
        // consumer reading only the counts would see a clean run over a component that lost its
        // tests entirely.
        coverageLost = report.CoverageLost,

        // What DEC0011 took whole, and which changes made it. Without it a partial run over most of
        // a codebase and one over a handful of files write the same scope, and a CI summary built
        // on this file cannot tell its reader which it was (#22).
        widened = report.Widened.Select(project => new
        {
            project = project.Project,
            because = project.Because,
        }).ToArray(),

        // The share of the undetected mutants in files the change did not touch. Still counted
        // above and still failing the verdict; null on a full run, which has no change to be
        // outside of.
        undetectedFromWidening = report.Scope.IsPartial ? (int?)report.UndetectedFromWidening : null,

        // Components the earlier state was rebuilt without. Published beside the verdict rather
        // than among the warnings because it is not advice: a consumer that reads hasUndetected
        // without reading this can act on a comparison the run itself does not stand behind.
        comparisonIsIncomplete = report.ComparisonIsIncomplete,
        unreadComponents = report.UnreadComponents,

        // Published so a CI job can act on them rather than only a person reading a terminal.
        warnings = report.Warnings.Select(warning => warning.Text).ToArray(),
        byMutator = report.ByMutator.Select(family => Describe(family, report)).ToArray(),
        mutants = report.Results.Select(Describe).ToArray(),
    };

    private static object Describe(RunSeries series) => new
    {
        id = series.Id,
        inherited = series.Inherited,
        runs = series.Runs.Select(run => new
        {
            started = run.Started,
            toolVersion = run.ToolVersion,
            runtime = run.Runtime,
            operatingSystem = run.OperatingSystem,
            processorCount = run.ProcessorCount,
            workerCount = run.WorkerCount,
            verdicts = run.Verdicts,
            killsReVerifiedAlone = run.KillsReVerified,

            // The files a run did not count when it read its tree, and the patterns that named them
            // (DEC0025): a report says the tree was not exactly its commit, and which files made the
            // difference, rather than presenting the run as if it were.
            ignoreUntracked = run.IgnoreUntracked,
            ignoredUntracked = run.IgnoredUntracked,
        }).ToArray(),
    };

    private static object Describe(RunScope scope) => new
    {
        mode = scope.IsPartial ? "partial" : "full",

        // Whole, and as whatever supplied them resolved them - not as the user wrote them. A name
        // like 'main' means something different next week, and a report that records it has recorded
        // nothing anyone can go back to. Kept at full length here, where nothing has to fit on a
        // line, so the two states stay usable for reproducing the selection.
        comparedFrom = scope.ComparedFrom,
        comparedTo = scope.ComparedTo,

        // A run measures what it builds, which is not always exactly the state it names.
        comparedToIsExact = scope.ComparedToIsExact,

        // Null rather than zero when the run stopped before it had read the change: zero would say
        // that nothing changed, which nothing established.
        changedFiles = scope.IsResolved ? (int?)scope.ChangedFiles : null,
    };

    private static object Describe(RunEnvironment environment) => new
    {
        runtime = environment.Runtime,
        operatingSystem = environment.OperatingSystem,
        processorCount = environment.ProcessorCount,
        workerCount = environment.WorkerCount,
        testFramework = environment.TestFramework,

        // Without this a mutant reported as timed out cannot be explained after the fact.
        timeoutBudgetSeconds = environment.TimeoutBudgetsInSeconds,

        // The three inputs, not just the answer: a budget nobody can recompute is a number a reader
        // has to take on trust, and a timeout cannot be explained after the fact without it.
        baselineSecondsAlone = environment.BaselineSecondsAlone,
        killsReVerifiedAlone = environment.KillsReVerified,
        timeoutBaselineFactor = environment.TimeoutBudgets.Select(b => b.Factor).Distinct(),
        timeoutMarginSeconds = environment.TimeoutBudgets.Select(b => b.Margin.TotalSeconds).Distinct(),
    };

    private static object Describe(MutatorSummary family, MutationTestReport report) => new
    {
        mutator = family.Mutator.ToString(),
        mutants = family.Total,
        detected = family.Detected,
        undetected = family.Undetected,
        untestable = family.Untestable,
        ignored = family.Ignored,

        // Null on a partial run for the same reason the run's own score is: a per-family ratio over
        // a population defined by the diff is the whole objection in miniature. The counts stay.
        score = HasNoScore(report) || family.Score.IsUndefined
            ? null
            : (double?)Math.Round(family.Score.Value, 4),
    };

    /// <summary>True when the run's population is not one a percentage can describe.</summary>
    private static bool HasNoScore(MutationTestReport report) => !report.HasScore;

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

        // Every rule of --ignore-calls that silences this mutant; empty for one that was tested.
        ignoredBy = result.Mutant.IgnoredBy.ToArray(),

        // With the three fields above and these names, a kill can be reproduced by hand: put the
        // mutation in the file at that position, run these tests, watch them fail. A verdict nobody
        // can reproduce is a verdict nobody can dispute.
        killedBy = result.KilledBy.Select(test => test.ToString()).ToArray(),

        // Null on every mutant that was never re-tested, and on every one whose verdict held.
        disagreement = result.Disagreement,
        overturned = result.Overturned,
        detail = result.Detail,
    };
}
