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

        RunSettings settings;

        try
        {
            // Read from the directory the run was pointed at, so a project's habits travel with its
            // code. Usage is not printed here: the message names a file and a line in it, and
            // twenty-five lines of options after that would bury it.
            settings = RunSettings.From(options, ConfigurationFile.LoadFrom(options.Directory));
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);

            return ExitCode.BadUsage;
        }

        try
        {
            MutationTestReport report = await RunAsync(settings).ConfigureAwait(false);

            ConsoleReportWriter.Write(Console.Out, report);
            WriteJsonReport(settings, report);

            return Verdict(settings, report);
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The run itself finished; writing the report is what failed - an unwritable directory,
            // a path that is a directory, a full disk. That is still a tool failure and belongs on
            // the documented exit code, not in an unhandled exception: exit codes are this tool's
            // contract with CI, and a stack trace tells a build script nothing it can act on.
            Console.Error.WriteLine(
                $"The mutation run finished, but its report could not be written: {exception.Message}");

            return ExitCode.CouldNotRun;
        }
    }

    private static async Task<MutationTestReport> RunAsync(RunSettings settings)
    {
        using var progress = new ConsoleProgressReporter(
            Console.Error, rewritesInPlace: !Console.IsErrorRedirected);

        MutationTestReport report = await MutationTesting
            .RunAsync(
                settings.Directory, settings.Configuration, settings.WorkerCount,
                settings.MeasureCoverage, settings.Exclude, settings.Mutators,
                settings.WithoutMutators, settings.VerifyKills, progress)
            .ConfigureAwait(false);

        // Progress goes to stderr and the report to stdout, so piping the report somewhere useful
        // does not drag the progress line along with it.
        progress.Dispose();

        return report;
    }

    /// <summary>Decides what to tell the shell, and says why on the way out.</summary>
    private static int Verdict(RunSettings settings, MutationTestReport report)
    {
        if (settings.Threshold is not { } threshold)
        {
            return ExitCode.Success;
        }

        string wanted = settings.ThresholdText;

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

    private static void WriteJsonReport(RunSettings settings, MutationTestReport report)
    {
        if (settings.JsonReportPath is not { } jsonPath)
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

            Settings:
              A project may keep its habits in killmutants.json, in the directory above. Every
              option below has a key there - configuration, exclude, mutators, without, parallel,
              coverage, breakAt, verifyKills, reportJson - and anything given on the command
              line wins.

            Options:
              -c, --configuration <cfg> Build configuration to analyse and run. Defaults to Release.
              -e, --exclude <pattern>   Leave a project or source file alone. Repeatable. Matched
                                        against the path relative to the directory above, written
                                        with '/'. Note that '*' also matches '/', so 'tests/*'
                                        covers everything beneath 'tests'.
              -m, --mutators <list>     Only run these mutator families, comma separated.
                  --without <list>      Leave these families out. Applied after --mutators.
                                        Pass 'none' to run every family even when the file
                                        excludes some.
              -p, --parallel <n>        Mutants to test at once. Defaults to half the processors.
                  --break-at <percent>  Exit with 1 if the mutation score falls below this.
                  --no-coverage         Run every test for every mutant, instead of only the ones
                                        that reach it.
                  --report-json <path>  Also write the report as JSON, for CI and tooling.
                  --verify-kills <n>    Test n of the mutants reported killed a second time, on
                                        their own, and report any verdict that does not repeat.
                                        Costs one test run each. Defaults to 0.
              -h, --help                Show this help.

            Exit codes:
              0   Ran, and met the threshold if one was given.
              1   Ran, but the score is below --break-at.
              2   Could not run: see the message on standard error.
              64  The command line was not understood.
            """);
    }
}
