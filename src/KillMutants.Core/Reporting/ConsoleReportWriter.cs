using System.Globalization;
using KillMutants.Mutations;

namespace KillMutants.Reporting;

/// <summary>Writes a run's outcome to a text writer.</summary>
public static class ConsoleReportWriter
{
    /// <summary>
    /// The longest a mutant's text is shown in the console: wide enough for an ordinary expression,
    /// and two of them beside a position still fit a wide terminal.
    /// </summary>
    internal const int ShownLength = 60;

    /// <summary>Writes the report.</summary>
    public static void Write(TextWriter writer, MutationTestReport report)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(report);

        writer.WriteLine("KillMutants");
        writer.WriteLine();

        WriteStopCaveat(writer, report);
        WriteSetAside(writer, report);
        WriteSeriesCaveat(writer, report);
        WriteComparisonCaveat(writer, report);

        // The findings come before the totals because they are the point of the run: a survivor
        // names code the tests do not really check, and an uncovered mutant names code they do not
        // reach at all. The numbers only summarise them.
        WriteFindings(writer, report, MutantStatus.Survived, "Survived");
        WriteFindings(writer, report, MutantStatus.NoCoverage, "No coverage");

        WriteTotals(writer, report);
        WriteFamilies(writer, report);
        WriteIgnored(writer, report);
        WriteWarnings(writer, report);

        writer.WriteLine();
        WriteScopeAndScore(writer, report);

        if (report.Duration > TimeSpan.Zero)
        {
            writer.WriteLine($"Elapsed: {Format(report.Duration)}");
        }

        if (report.Environment is { } environment)
        {
            writer.WriteLine($"Ran on: {environment}");
        }
    }

    /// <summary>
    /// Says what population was inspected, and gives the score only where one means something.
    /// </summary>
    /// <remarks>
    /// A full run ends with its score, as it always has. A partial run ends with its scope instead
    /// and with the reason it prints no percentage - stated in the report rather than left to
    /// documentation, because the reader who would draw a trend from two partial runs is exactly the
    /// reader who never opened the documentation. See DEC0010.
    /// </remarks>
    /// <summary>Says the comparison was incomplete, before anything it makes provisional.</summary>
    /// <remarks>
    /// At the top of the report because that is what "before" means here, and review found the first
    /// attempt asserting it while doing the opposite: the text sat in the scope section, which this
    /// writer emits last, so a caveat about how to read the findings arrived after the reader had
    /// already read them - and it said "the mutants below" about mutants printed above it. The
    /// wording was checked and the call order was not.
    /// </remarks>
    private static void WriteComparisonCaveat(TextWriter writer, MutationTestReport report)
    {
        if (!report.ComparisonIsIncomplete)
        {
            return;
        }

        foreach (string line in Wrap(
                     "Comparison incomplete: the earlier state was reconstructed without " +
                     string.Join(", ", report.UnreadComponents) +
                     ", whose contents are not in this clone. The mutants below were really run and " +
                     "their results hold; what the comparison decided - which code was selected, and " +
                     "any coverage reported lost - does not.",
                     width: 88))
        {
            writer.WriteLine(line);
        }

        writer.WriteLine();
    }

    /// <summary>Says the run was stopped, before anything that being stopped leaves incomplete.</summary>
    /// <remarks>
    /// First, for the reason the comparison caveat below it is: every count and finding that follows
    /// is a statement about the mutants the run reached, and a reader has to know that before reading
    /// them rather than four screens later.
    /// </remarks>
    private static void WriteStopCaveat(TextWriter writer, MutationTestReport report)
    {
        if (report.Stopped is not { } stopped)
        {
            return;
        }

        string reached = stopped.Selected is { } selected
            ? $"{Format(report.Total)} of the {Format(selected)} mutant(s) it selected have a verdict"
            : "it had not yet selected a single mutant";

        foreach (string line in Wrap(
                     $"Stopped before the end: {reached}. The verdicts below were really reached and " +
                     "hold; the mutants without one were never asked about, and nothing here says " +
                     "what they would have been.",
                     width: 88))
        {
            writer.WriteLine(line);
        }

        writer.WriteLine();
    }

    /// <summary>Names what the run could not measure, and why, before anything it leaves partial.</summary>
    /// <remarks>
    /// At the top, for the reason the stop caveat is: every count below is about the part that was
    /// measured, and the reader has to know which part that is first. The reason is the one the run
    /// would have stopped with before DEC0022, so nothing it said is lost by carrying on.
    /// </remarks>
    private static void WriteSetAside(TextWriter writer, MutationTestReport report)
    {
        if (report.MeasuredEverything)
        {
            return;
        }

        writer.WriteLine($"Set aside ({Format(report.SetAside.Count)}) - not measured, so no gate passes on this run:");

        foreach (SetAside part in report.SetAside)
        {
            writer.WriteLine();
            writer.WriteLine($"  {part.Name} ({Describe(part.Stage)})");

            foreach (string line in part.Reason.ReplaceLineEndings("\n").Split('\n'))
            {
                writer.WriteLine(line.Length == 0 ? string.Empty : $"    {line}");
            }
        }

        writer.WriteLine();
    }

    private static string Describe(SetAsideStage stage) => stage switch
    {
        SetAsideStage.Discovery => "could not be read",
        SetAsideStage.Build => "did not build",
        SetAsideStage.Analysis => "could not be analysed",
        SetAsideStage.Baseline => "red baseline",
        SetAsideStage.Coverage => "coverage could not be measured",
        _ => stage.ToString(),
    };

    /// <summary>Says the report is the sum of several runs, before the counts that sum them.</summary>
    /// <remarks>
    /// A run that inherited everything writes a complete report in minutes, having tested almost
    /// nothing itself; a reader who takes its counts for one run's would misread how the measurement
    /// was made. Said once, here, and the rest of the report reads as it always has.
    /// </remarks>
    private static void WriteSeriesCaveat(TextWriter writer, MutationTestReport report)
    {
        if (report.Series is not { } series)
        {
            return;
        }

        int earlier = series.Runs.Count - 1;
        int reached = series.Runs.Count > 0 ? series.Runs[^1].Verdicts : 0;

        string line = earlier <= 0
            ? $"Series '{series.Id}': first run, nothing inherited."
            : $"Series '{series.Id}': run {Format(series.Runs.Count)}, {Format(series.Inherited)} " +
              $"verdict(s) inherited from {Format(earlier)} earlier run(s), {Format(reached)} reached " +
              "by this run. The counts below sum the whole series.";

        foreach (string wrapped in Wrap(line, width: 88))
        {
            writer.WriteLine(wrapped);
        }

        writer.WriteLine();
    }

    private static void WriteScopeAndScore(TextWriter writer, MutationTestReport report)
    {
        if (!report.Scope.IsPartial)
        {
            if (report.HasScore && report.MeasuredEverything)
            {
                writer.WriteLine($"Mutation score: {report.Score}{IgnoredBeside(report)}");

                return;
            }

            if (report.HasScore && report.Total == 0)
            {
                writer.WriteLine("No mutation score: nothing was measured; every part with mutants was set aside above.");

                return;
            }

            // The part measured has a score of its own, and it is labelled as such so it is not
            // quoted as the project's (DEC0022).
            if (report.HasScore)
            {
                foreach (string line in Wrap(
                             $"Mutation score: {report.Score}{IgnoredBeside(report)}, over the part measured only - " +
                             $"{Format(report.SetAside.Count)} part(s) set aside above. See DEC0022.",
                             width: 88))
                {
                    writer.WriteLine(line);
                }

                return;
            }

            if (report.IsComplete)
            {
                foreach (string line in Wrap(
                             "No mutation score: a project set aside at discovery may have been the " +
                             "only suite reaching some code, so the population a score would cover " +
                             "is not known. See DEC0022.",
                             width: 88))
                {
                    writer.WriteLine(line);
                }

                return;
            }

            foreach (string line in Wrap(
                         "No mutation score: the run stopped before the end, and the mutants it " +
                         "judged are the ones it reached first rather than a sample of the codebase. " +
                         "See DEC0013.",
                         width: 88))
            {
                writer.WriteLine(line);
            }

            return;
        }

        // A change the run never got as far as reading has no file count to give, and printing the
        // zero it holds would say that nothing changed.
        writer.WriteLine(report.Scope.IsResolved
            ? $"Scope: {report.Scope} ({Format(report.Scope.ChangedFiles)} " +
              $"{Plural(report.Scope.ChangedFiles, "file")} changed)"
            : $"Scope: {report.Scope}");

        // Straight after the scope line, because that line describes the diff alone: read without
        // this, a run DEC0011 took over most of a codebase says it was a run over a few files (#22).
        if (report.Widened.Count > 0)
        {
            foreach (string line in Wrap(Widening(report), width: 88))
            {
                writer.WriteLine(line);
            }
        }

        foreach (string line in Wrap(
                     "No mutation score: a partial run's population is the change itself, chosen " +
                     "against an earlier state, per run, so a percentage over it cannot be compared " +
                     "with any other run. See DEC0010.",
                     width: 88))
        {
            writer.WriteLine(line);
        }

        if (report.LostCoverage)
        {
            foreach (string line in Wrap(
                         "Coverage lost: no test project reaches " +
                         string.Join(", ", report.CoverageLost.Select(Path.GetFileNameWithoutExtension)) +
                         " any more, so this run had nothing to ask about it.",
                         width: 88))
            {
                writer.WriteLine(line);
            }
        }

        // First branch of the same chain rather than a paragraph beside it, and review found why it
        // has to be: the warning above was printed and then execution fell through to a verdict, so
        // a run whose mutants all died said "Comparison incomplete" and, four lines later, "Verdict:
        // no undetected mutant in the selected scope". Every branch below reads as a settled answer
        // - "nothing produces a mutant" included - and exactly one line may be printed here.
        //
        // A stopped run comes first: it has not judged its whole selection, so nothing below - not
        // even the comparison - could settle the answer for the mutants it never reached.
        if (!report.IsComplete)
        {
            writer.WriteLine(report.Total == 0
                ? "No verdict: the run stopped before it had judged a single mutant."
                : $"No verdict: {Format(report.Undetected)} of {Format(report.Total)} mutant(s) " +
                  "judged were undetected, and the run stopped before it had judged the rest.");
        }
        else if (report.ComparisonIsIncomplete)
        {
            writer.WriteLine(report.Total == 0
                ? "No verdict: nothing in the change produced a mutant, and what selected it was " +
                  "compared against a state this run could not fully read."
                : $"No verdict: {Format(report.Undetected)} of {Format(report.Total)} mutant(s) " +
                  "undetected, against a comparison this run could not complete.");
        }
        else if (report.Total == 0)
        {
            writer.WriteLine("Nothing in the change produces a mutant.");
        }
        else if (report.IsInconclusive)
        {
            writer.WriteLine(
                "Inconclusive: mutants were generated and not one of them could be tested.");
        }
        else
        {
            writer.WriteLine(report.HasUndetected
                ? $"Verdict: {Format(report.Undetected)} undetected mutant(s) in the selected scope."
                : "Verdict: no undetected mutant in the selected scope.");
        }
    }

    /// <summary>Lists the mutants of one status, grouped by the file they sit in.</summary>
    private static void WriteFindings(
        TextWriter writer,
        MutationTestReport report,
        MutantStatus status,
        string heading)
    {
        MutantResult[] findings = [.. report.Results.Where(result => result.Status == status)];

        if (findings.Length == 0)
        {
            return;
        }

        writer.WriteLine($"{heading} ({Format(findings.Length)})");

        foreach (IGrouping<string, MutantResult> inFile in findings
                     .GroupBy(result => result.Mutant.Location.FilePath)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            writer.WriteLine();
            writer.WriteLine($"  {Path.GetFileName(inFile.Key)}");

            MutantResult[] ordered = [.. inFile
                .OrderBy(result => result.Mutant.Location.Line)
                .ThenBy(result => result.Mutant.Location.Character)];

            // Widths are computed per file so each block lines up without a fixed guess, and on the
            // texts as shown rather than as written: a single long literal used to widen every line
            // of its file to its own length (#52).
            int positionWidth = ordered.Max(result => Position(result).Length);
            int originalWidth = ordered.Max(result => Shown(result.Mutant.OriginalText).Length);
            int mutatedWidth = ordered.Max(result => Shown(result.Mutant.MutatedText).Length);

            foreach (MutantResult finding in ordered)
            {
                writer.WriteLine(
                    $"    {Position(finding).PadLeft(positionWidth)}  " +
                    $"{Shown(finding.Mutant.OriginalText).PadRight(originalWidth)} -> " +
                    $"{Shown(finding.Mutant.MutatedText).PadRight(mutatedWidth)}  [{finding.Mutant.Mutator}]");
            }
        }

        writer.WriteLine();
    }

    /// <summary>
    /// A mutant's text as one line of at most <see cref="ShownLength"/> characters.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The console is for reading, and a finding is found again by its file and position. On
    /// <c>Reefact/first-class-errors</c> one 1253-character description literal padded every
    /// finding of its file to about 1290 columns, and 278 lines of the report passed 400 (#52). The
    /// whole text is in the JSON report, which is where a tool reads it.
    /// </para>
    /// <para>
    /// Whitespace runs, line breaks included, become one space first: <c>Statement</c> removes a
    /// whole statement, a lambda's body and all, and a finding printed over ten lines no longer
    /// reads as one line of a table.
    /// </para>
    /// </remarks>
    internal static string Shown(string text)
    {
        string flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return flat.Length <= ShownLength ? flat : string.Concat(flat.AsSpan(0, ShownLength - 1), "…");
    }

    private static void WriteTotals(TextWriter writer, MutationTestReport report)
    {
        writer.WriteLine($"Mutants: {Format(report.Total)}");
        writer.WriteLine($"Killed: {Format(report.Killed)}");
        writer.WriteLine($"Survived: {Format(report.Survived)}");

        // The remaining statuses are shown only when they happened, so a clean run stays short.
        if (report.TimedOut > 0)
        {
            writer.WriteLine($"Timed out: {Format(report.TimedOut)}");
        }

        if (report.CompileErrors > 0)
        {
            writer.WriteLine($"Compile errors: {Format(report.CompileErrors)}");
        }

        if (report.Uncovered > 0)
        {
            // Worth naming rather than hiding: it says the code has no tests at all, which is a
            // different and often more urgent finding than a surviving mutant.
            writer.WriteLine($"No coverage: {Format(report.Uncovered)}");
        }

        if (report.Ignored > 0)
        {
            writer.WriteLine($"Ignored: {Format(report.Ignored)} (by the project's rules)");
        }
    }

    /// <summary>
    /// What the score line says beside the percentage when the project silenced something: the one
    /// reader a percentage can mislead is the one who reads nothing else, so the count goes where
    /// that reader looks (DEC0026).
    /// </summary>
    private static string IgnoredBeside(MutationTestReport report) =>
        report.Ignored > 0
            ? $" ({Format(report.Ignored)} ignored by the project's rules)"
            : string.Empty;

    /// <summary>
    /// Each rule of <c>--ignore-calls</c> with what it silences, a rule that silenced nothing first
    /// of all: that one is a method renamed or a pattern that never matched, and obeying it in
    /// silence would be the tool lying by omission about what the project asked (DEC0026). What the
    /// zero says is exactly what was counted - no mutant of this run under that rule - and not that
    /// the pattern recognises no call: a call with nothing to mutate is recognised and silences
    /// nothing (review of #74).
    /// </summary>
    private static void WriteIgnored(TextWriter writer, MutationTestReport report)
    {
        if (report.IgnoredByRule.Count == 0)
        {
            return;
        }

        int ruleWidth = report.IgnoredByRule.Max(rule => rule.Rule.Length);

        writer.WriteLine();
        writer.WriteLine($"Ignored by the project's rules ({Format(report.Ignored)})");
        writer.WriteLine();

        foreach (IgnoredRule rule in report.IgnoredByRule)
        {
            writer.WriteLine(
                $"  {rule.Rule.PadRight(ruleWidth)}  " +
                (rule.Mutants == 0
                    ? "silenced no mutant of this run"
                    : $"{Format(rule.Mutants)} {Plural(rule.Mutants, "mutant")}"));
        }

        // Said only when it happens, so the rows and the heading are never read as disagreeing.
        if (report.IgnoredByRule.Sum(rule => rule.Mutants) > report.Ignored)
        {
            writer.WriteLine("  (a mutant silenced by several rules is counted under each)");
        }
    }

    /// <summary>
    /// Lists what each mutator family cost and what it caught.
    /// </summary>
    /// <remarks>
    /// The one number a reader can act on directly. A family producing a third of the mutants and
    /// detecting a tenth of them is either the most valuable finding in the report or the least,
    /// depending on the project - and either way it is worth seeing before deciding what to pass to
    /// <c>--without</c>. Shown only when there is more than one family, so a narrowed run stays short.
    /// </remarks>
    private static void WriteFamilies(TextWriter writer, MutationTestReport report)
    {
        if (report.ByMutator.Count < 2)
        {
            return;
        }

        int nameWidth = report.ByMutator.Max(family => family.Mutator.ToString().Length);
        int totalWidth = report.ByMutator.Max(family => Format(family.Total).Length);
        int detectedWidth = report.ByMutator.Max(family => Format(family.Detected).Length);

        writer.WriteLine();
        writer.WriteLine("By mutator");
        writer.WriteLine();

        foreach (MutatorSummary family in report.ByMutator)
        {
            // The per-family percentage is the whole-run score in miniature, and a partial run has
            // no population for it to be a percentage of either - nor does a stopped one. The counts
            // stay; the ratio goes.
            string score = !report.HasScore
                ? string.Empty
                : $"  ({family.Score})";

            string ignored = family.Ignored > 0
                ? $", {Format(family.Ignored)} ignored"
                : string.Empty;

            writer.WriteLine(
                $"  {family.Mutator.ToString().PadRight(nameWidth)}  " +
                $"{Format(family.Total).PadLeft(totalWidth)} {Plural(family.Total, "mutant")}, " +
                $"{Format(family.Detected).PadLeft(detectedWidth)} detected{score}{ignored}");
        }
    }

    /// <summary>
    /// Says what the score rests on, immediately before printing it.
    /// </summary>
    /// <remarks>
    /// Placed here on purpose. A reader who scrolls to the last line and stops must not be able to
    /// take a number away without the sentence that qualifies it, which is exactly how a report
    /// reading "100%" over a component half of which was never judged goes unquestioned.
    /// </remarks>
    private static void WriteWarnings(TextWriter writer, MutationTestReport report)
    {
        foreach (RunWarning warning in report.Warnings)
        {
            writer.WriteLine();

            foreach (string line in Wrap(warning.Text, width: 88))
            {
                writer.WriteLine($"! {line}");
            }
        }
    }

    /// <summary>Breaks a sentence into lines a terminal can hold.</summary>
    private static IEnumerable<string> Wrap(string text, int width)
    {
        var line = new System.Text.StringBuilder();

        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width)
            {
                yield return line.ToString();
                line.Clear();
            }

            line.Append(line.Length > 0 ? " " : string.Empty).Append(word);
        }

        if (line.Length > 0)
        {
            yield return line.ToString();
        }
    }

    /// <summary>Which projects a partial run took whole, what made it, and what that added.</summary>
    private static string Widening(MutationTestReport report)
    {
        // By name, which is how a reader knows a project, and by path where two share one: review
        // found "Core and Core were mutated whole" naming nothing at all.
        string[] paths = [.. report.Widened.Select(project => project.Project)];
        string[] projects =
        [
            .. paths.Select(path => paths.Count(other => SameName(other, path)) > 1
                ? path
                : Path.GetFileNameWithoutExtension(path)),
        ];
        string[] causes =
        [
            .. report.Widened
                .SelectMany(project => project.Because)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];

        string because = causes.Length == 0
            ? string.Empty
            : $", because the change touches {Listed(causes, named: 3)}";
        string widened =
            $"Widened: {Listed(projects, named: 5)} {(projects.Length == 1 ? "was" : "were")} " +
            $"mutated whole rather than file by file{because} (DEC0011).";

        // Left out when nothing went undetected: "0 of the 0" would be a sentence about nothing.
        if (report.Undetected == 0)
        {
            return widened;
        }

        // Agreed with the total, which is what the sentence is about: "1 of the 1 undetected mutant
        // is in a file", "1 of the 2 undetected mutants are in files".
        string where = report.Undetected == 1
            ? "is in a file the change did not touch"
            : "are in files the change did not touch";

        string share =
            $"{Format(report.UndetectedFromWidening)} of the {Format(report.Undetected)} " +
            Plural(report.Undetected, "undetected mutant");

        return $"{widened} {share} {where}.";
    }

    private static bool SameName(string one, string other) =>
        string.Equals(
            Path.GetFileNameWithoutExtension(one),
            Path.GetFileNameWithoutExtension(other),
            StringComparison.Ordinal);

    /// <summary>
    /// Names in a sentence, the first <paramref name="named"/> of them, and how many more there are.
    /// </summary>
    private static string Listed(string[] names, int named)
    {
        if (names.Length > named)
        {
            return $"{string.Join(", ", names.Take(named))} and {Format(names.Length - named)} more";
        }

        return names.Length == 1
            ? names[0]
            : $"{string.Join(", ", names.Take(names.Length - 1))} and {names[^1]}";
    }

    private static string Plural(int count, string noun) => count == 1 ? noun : noun + "s";

    private static string Position(MutantResult result) =>
        $"{Format(result.Mutant.Location.Line)}:{Format(result.Mutant.Location.Character)}";

    private static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Format(TimeSpan duration) =>
        duration.TotalMinutes >= 1
            ? $"{duration.TotalMinutes.ToString("0.0", CultureInfo.InvariantCulture)} min"
            : $"{duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s";
}
