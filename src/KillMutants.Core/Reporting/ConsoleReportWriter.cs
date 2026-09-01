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

        writer.WriteLine();
        writer.WriteLine($"Mutation score: {report.Score}");

        if (report.Duration > TimeSpan.Zero)
        {
            writer.WriteLine($"Elapsed: {Format(report.Duration)}");
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

    private static string Position(MutantResult result) =>
        $"{Format(result.Mutant.Location.Line)}:{Format(result.Mutant.Location.Character)}";

    private static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Format(TimeSpan duration) =>
        duration.TotalMinutes >= 1
            ? $"{duration.TotalMinutes.ToString("0.0", CultureInfo.InvariantCulture)} min"
            : $"{duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s";
}
