using KillMutants;
using KillMutants.Execution;
using KillMutants.Projects;
using KillMutants.Reporting;
using KillMutants.Testing;

namespace KillMutants.Cli;

/// <summary>The <c>dotnet killmutants</c> entry point.</summary>
internal static class Program
{
    private const int Success = 0;
    private const int Failure = 1;

    private static async Task<int> Main(string[] args)
    {
        if (args.Any(argument => argument is "-h" or "--help"))
        {
            WriteUsage();

            return Success;
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

            return Failure;
        }

        try
        {
            using var progress = new ConsoleProgressReporter(
                Console.Error, rewritesInPlace: !Console.IsErrorRedirected);

            MutationTestReport report = await MutationTesting
                .RunAsync(
                    options.Directory, options.Configuration, options.WorkerCount,
                    options.MeasureCoverage, progress)
                .ConfigureAwait(false);

            // Progress goes to stderr and the report to stdout, so piping the report somewhere
            // useful does not drag the progress line along with it.
            progress.Dispose();

            ConsoleReportWriter.Write(Console.Out, report);

            if (options.JsonReportPath is { } jsonPath)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);

                using var json = new StreamWriter(jsonPath);
                JsonReportWriter.Write(json, report);
            }

            return Success;
        }
        catch (Exception exception) when (
            exception is ProjectAnalysisException or BaselineVerificationException or TestExecutionException)
        {
            // These report a problem the user must fix, and their messages say what it is.
            // A stack trace would add noise, not information.
            Console.Error.WriteLine(exception.Message);

            return Failure;
        }
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
                  --no-coverage         Run every test for every mutant, instead of only the ones
                                        that reach it.
                  --report-json <path>  Also write the report as JSON, for CI and tooling.
              -h, --help                Show this help.
            """);
    }
}
