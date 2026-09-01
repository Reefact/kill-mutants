using System.Globalization;
using KillMutants.Coverage;
using KillMutants.Execution;
using KillMutants.Projects;
using KillMutants.Reporting;
using KillMutants.Testing;

namespace KillMutants.Cli;

/// <summary>The <c>dotnet killmutants</c> entry point.</summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Any(argument => argument is "-h" or "--help"))
        {
            WriteUsage();

            return ExitCode.Success;
        }

        CommandLineOptions options;

        try
        {
            options = CommandLineOptions.Parse(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            WriteUsage();

            return ExitCode.BadUsage;
        }

        try
        {
            MutationTestReport report = await RunAsync(options).ConfigureAwait(false);

            ConsoleReportWriter.Write(Console.Out, report);
            WriteJsonReport(options, report);

            return Verdict(options, report);
        }
        catch (Exception exception) when (
            exception is ProjectAnalysisException or BaselineVerificationException
                or TestExecutionException or CoverageException)
        {
            // These report a problem the user must fix, and their messages say what it is.
            // A stack trace would add noise, not information.
            Console.Error.WriteLine(exception.Message);

            return ExitCode.CouldNotRun;
        }
    }

    private static async Task<MutationTestReport> RunAsync(CommandLineOptions options)
    {
        using var progress = new ConsoleProgressReporter(
            Console.Error, rewritesInPlace: !Console.IsErrorRedirected);

        MutationTestReport report = await MutationTesting
            .RunAsync(
                options.Directory, options.Configuration, options.WorkerCount,
                options.MeasureCoverage, progress)
            .ConfigureAwait(false);

        // Progress goes to stderr and the report to stdout, so piping the report somewhere useful
        // does not drag the progress line along with it.
        progress.Dispose();

        return report;
    }

    /// <summary>Decides what to tell the shell, and says why on the way out.</summary>
    private static int Verdict(CommandLineOptions options, MutationTestReport report)
    {
        if (options.Threshold is not { } threshold)
        {
            return ExitCode.Success;
        }

        string wanted = threshold.ToString("0.##", CultureInfo.InvariantCulture);

        // A run that could test nothing has not demonstrated anything, so it cannot satisfy a
        // threshold. Reporting success here would let a misconfigured job go green forever.
        if (report.Score.IsUndefined)
        {
            Console.Error.WriteLine(
                $"No mutant could be tested, so the {wanted}% threshold cannot be shown to be met.");

            return ExitCode.ScoreBelowThreshold;
        }

        double achieved = report.Score.Value * 100d;

        if (achieved + 0.005 < threshold)
        {
            Console.Error.WriteLine(
                $"Mutation score {report.Score} is below the {wanted}% threshold.");

            return ExitCode.ScoreBelowThreshold;
        }

        return ExitCode.Success;
    }

    private static void WriteJsonReport(CommandLineOptions options, MutationTestReport report)
    {
        if (options.JsonReportPath is not { } jsonPath)
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);

        using var json = new StreamWriter(jsonPath);
        JsonReportWriter.Write(json, report);
    }

    private static void WriteUsage()
    {
        Console.WriteLine(
            """
            KillMutants - a modern, opinionated mutation testing tool for .NET, built for xUnit 4.

            Usage:
              dotnet killmutants [directory] [options]

            Arguments:
              directory                 Where to look for projects. Defaults to the current directory.

            Options:
              -c, --configuration <cfg> Build configuration to analyse and run. Defaults to Release.
              -p, --parallel <n>        Mutants to test at once. Defaults to half the processors.
                  --break-at <percent>  Exit with 1 if the mutation score falls below this.
                  --no-coverage         Run every test for every mutant, instead of only the ones
                                        that reach it.
                  --report-json <path>  Also write the report as JSON, for CI and tooling.
              -h, --help                Show this help.

            Exit codes:
              0   Ran, and met the threshold if one was given.
              1   Ran, but the score is below --break-at.
              2   Could not run: see the message on standard error.
              64  The command line was not understood.
            """);
    }
}
