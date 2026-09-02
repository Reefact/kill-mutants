using System.Globalization;
using KillMutants.Mutations;

namespace KillMutants.Reporting;

/// <summary>Writes a run's outcome to a text writer.</summary>
public static class ConsoleReportWriter
{
    /// <summary>Writes the report.</summary>
    public static void Write(TextWriter writer, MutationTestReport report)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(report);

        writer.WriteLine("KillMutants");
        writer.WriteLine();

        // The findings come before the totals because they are the point of the run: a survivor
        // names code the tests do not really check, and an uncovered mutant names code they do not
        // reach at all. The numbers only summarise them.
        WriteFindings(writer, report, MutantStatus.Survived, "Survived");
        WriteFindings(writer, report, MutantStatus.NoCoverage, "No coverage");

        WriteTotals(writer, report);
        WriteFamilies(writer, report);
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
    private static void WriteScopeAndScore(TextWriter writer, MutationTestReport report)
    {
        if (!report.Scope.IsPartial)
        {
            writer.WriteLine($"Mutation score: {report.Score}");

            return;
        }

        writer.WriteLine(
            $"Scope: {report.Scope} ({Format(report.Scope.ChangedFiles)} " +
            $"{Plural(report.Scope.ChangedFiles, "file")} changed)");

        foreach (string line in Wrap(
                     "No mutation score: a partial run's population is the change itself, chosen " +
                     "against a base revision per run, so a percentage over it cannot be compared " +
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

        if (report.Total == 0)
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

            // Widths are computed per file so each block lines up without a fixed guess that a long
            // expression would blow out.
            int positionWidth = ordered.Max(result => Position(result).Length);
            int originalWidth = ordered.Max(result => result.Mutant.OriginalText.Length);
            int mutatedWidth = ordered.Max(result => result.Mutant.MutatedText.Length);

            foreach (MutantResult finding in ordered)
            {
                writer.WriteLine(
                    $"    {Position(finding).PadLeft(positionWidth)}  " +
                    $"{finding.Mutant.OriginalText.PadRight(originalWidth)} -> " +
                    $"{finding.Mutant.MutatedText.PadRight(mutatedWidth)}  [{finding.Mutant.Mutator}]");
            }
        }

        writer.WriteLine();
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
            // no population for it to be a percentage of either. The counts stay; the ratio goes.
            string score = report.Scope.IsPartial ? string.Empty : $"  ({family.Score})";

            writer.WriteLine(
                $"  {family.Mutator.ToString().PadRight(nameWidth)}  " +
                $"{Format(family.Total).PadLeft(totalWidth)} {Plural(family.Total, "mutant")}, " +
                $"{Format(family.Detected).PadLeft(detectedWidth)} detected{score}");
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

    private static string Plural(int count, string noun) => count == 1 ? noun : noun + "s";

    private static string Position(MutantResult result) =>
        $"{Format(result.Mutant.Location.Line)}:{Format(result.Mutant.Location.Character)}";

    private static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Format(TimeSpan duration) =>
        duration.TotalMinutes >= 1
            ? $"{duration.TotalMinutes.ToString("0.0", CultureInfo.InvariantCulture)} min"
            : $"{duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s";
}
