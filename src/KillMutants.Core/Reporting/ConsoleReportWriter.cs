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

        WriteSurvivors(writer, report);

        writer.WriteLine($"Mutants: {Format(report.Total)}");
        writer.WriteLine($"Killed: {Format(report.Killed)}");
        writer.WriteLine($"Survived: {Format(report.Survived)}");

        if (report.TimedOut > 0)
        {
            writer.WriteLine($"Timed out: {Format(report.TimedOut)}");
        }

        if (report.CompileErrors > 0)
        {
            writer.WriteLine($"Compile errors: {Format(report.CompileErrors)}");
        }

        writer.WriteLine();
        writer.WriteLine($"Mutation score: {report.Score}");
    }

    /// <summary>
    /// Survivors are the whole point of the run: they name the code the tests do not really check.
    /// They are listed before the totals so they are the first thing read.
    /// </summary>
    private static void WriteSurvivors(TextWriter writer, MutationTestReport report)
    {
        MutantResult[] survivors = [.. report.Results.Where(result => result.Status == MutantStatus.Survived)];

        if (survivors.Length == 0)
        {
            return;
        }

        writer.WriteLine("Survived:");

        foreach (MutantResult survivor in survivors)
        {
            writer.WriteLine(
                $"  {survivor.Mutant.Location}  {survivor.Mutant.OriginalText} -> {survivor.Mutant.MutatedText}" +
                $"  [{survivor.Mutant.Mutator}]");
        }

        writer.WriteLine();
    }

    private static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);
}
