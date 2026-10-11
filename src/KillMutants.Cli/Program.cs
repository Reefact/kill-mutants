using System.Globalization;
using KillMutants.Coverage;
using KillMutants.Execution;
using KillMutants.Git;
using KillMutants.Projects;
using KillMutants.Reporting;
using KillMutants.Selection;
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

        // Armed before the run begins, so the deadline counts from as near the tool's start as it
        // can - which is what a CI job's own budget is measured against.
        using var stop = new RunStop(settings.StopAfter);

        VerdictLog? log;

        try
        {
            log = settings.JsonReportPath is { } jsonPath ? OpenVerdictLog(jsonPath) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Refused before the run rather than discovered during it, when the first verdict would
            // fail to land after a build, a baseline and a coverage pass had all been paid for.
            Console.Error.WriteLine(
                $"The report's files could not be prepared for this run: {exception.Message}");

            return ExitCode.CouldNotRun;
        }

        using (log)
        {
            MutationTestReport report;

            try
            {
                report = await RunAsync(settings, log, stop.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is ProjectAnalysisException or BaselineVerificationException
                    or TestExecutionException or CoverageException or ChangeSelectionException)
            {
                // These report a problem the user must fix, and their messages say what it is.
                // A stack trace would add noise, not information.
                Console.Error.WriteLine(exception.Message);

                return ExitCode.CouldNotRun;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A file the run itself needed - a sandbox, the verdict log on a full disk. Kept
                // apart from the report's own failure below, which happens after the measuring is
                // done and says so: this one happens before it is.
                Console.Error.WriteLine($"The mutation run could not continue: {exception.Message}");

                return ExitCode.CouldNotRun;
            }

            try
            {
                ConsoleReportWriter.Write(Console.Out, report);
                WriteJsonReport(settings, report);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The run itself finished; writing the report is what failed - an unwritable
                // directory, a path that is a directory, a full disk. That is still a tool failure
                // and belongs on the documented exit code, not in an unhandled exception: exit codes
                // are this tool's contract with CI, and a stack trace tells a build script nothing
                // it can act on.
                Console.Error.WriteLine(
                    $"The mutation run finished, but its report could not be written: {exception.Message}");

                return ExitCode.CouldNotRun;
            }

            return Verdict(settings, report, stop);
        }
    }

    private static async Task<MutationTestReport> RunAsync(
        RunSettings settings, VerdictLog? log, CancellationToken stopToken)
    {
        using var progress = new ConsoleProgressReporter(
            Console.Error, rewritesInPlace: !Console.IsErrorRedirected);

        // The one place that knows both halves: the option names a git revision, so the CLI is
        // where a git-backed change source is built. The core is handed the source and never learns
        // what produced it.
        //
        // Not stopped part way: resolving a revision takes a moment, and a stop that arrives during
        // it is still honoured - the session finds the token already set and reports at once.
        // Stopping here instead would end the run with an exception rather than a report.
        IChangeSource? changes = settings.Since is null
            ? null
            : await GitChangeSource
                .ForAsync(settings.Directory, settings.Since, CancellationToken.None)
                .ConfigureAwait(false);

        MutationTestReport report = await MutationTesting
            .RunAsync(
                settings.Directory, settings.Configuration, settings.WorkerCount,
                settings.MeasureCoverage, settings.Exclude, settings.Mutators,
                settings.WithoutMutators, settings.VerifyKills, changes, progress, log,
                stopToken: stopToken)
            .ConfigureAwait(false);

        // Progress goes to stderr and the report to stdout, so piping the report somewhere useful
        // does not drag the progress line along with it.
        progress.Dispose();

        return report;
    }

    /// <summary>Decides what to tell the shell, and says why on the way out.</summary>
    private static int Verdict(RunSettings settings, MutationTestReport report, RunStop stop)
    {
        // Before everything else, because nothing else can be settled without it: whatever the
        // score or the verdict would have said, a stopped run did not get to every mutant it would
        // have said it about.
        if (!report.IsComplete)
        {
            return StoppedVerdict(settings, report, stop);
        }

        if (report.Scope.IsPartial)
        {
            return PartialVerdict(report);
        }

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

            return ExitCode.GateNotPassed;
        }

        double achieved = report.Score.Value * 100d;

        if (achieved + 0.005 < threshold)
        {
            Console.Error.WriteLine(
                $"Mutation score {report.Score} is below the {wanted}% threshold.");

            return ExitCode.GateNotPassed;
        }

        return ExitCode.Success;
    }

    /// <summary>
    /// The binary question a partial run answers: did the selected scope produce an undetected
    /// mutant?
    /// </summary>
    /// <remarks>
    /// <para>
    /// Always a gate, with no threshold to ask for: a partial run is asked for in order to judge a
    /// change, and a run that judged one and found untested behaviour has to say so with the code
    /// that means it. See DEC0010.
    /// </para>
    /// <para>
    /// Both undetected statuses fail it. A change that adds code nothing tests at all produces
    /// <c>NoCoverage</c> and not <c>Survived</c>, and that is the clearest case this exists to catch.
    /// A mutant the tool could not build stays outside, for the reason it stays outside the score:
    /// the suite was never asked about it.
    /// </para>
    /// <para>
    /// And a selection whose mutants were <em>all</em> untestable has not passed. It established
    /// nothing, and reporting success would let a misconfigured job stay green forever - the same
    /// rule ADR-0009 already applies to an undefined score against a threshold. A change with no
    /// mutants at all is a different thing and does pass, having nothing to answer for.
    /// </para>
    /// </remarks>
    private static int PartialVerdict(MutationTestReport report)
    {
        // Before everything else, because it is the only one that makes the others provisional. A
        // comparison built without part of the earlier state can lose an edge with no trace, so the
        // coverage it reports lost may be an artifact of what it could not read, and the coverage it
        // does not report may be real. Saying that first is saying what the run actually knows.
        if (report.ComparisonIsIncomplete)
        {
            Console.Error.WriteLine(
                "The earlier state was reconstructed without " +
                string.Join(", ", report.UnreadComponents) +
                ", whose contents are not in this clone, so this run could not establish what that " +
                "state's tests covered. The mutants it ran hold; what the comparison decided - the " +
                "selection, and any coverage reported lost - does not. Run " +
                "`git submodule update --init --recursive` and try again, or run without --since " +
                "to measure the whole codebase.");

            return ExitCode.GateNotPassed;
        }

        // Then the one the counts cannot show: a project with no tests reaching it has no mutants
        // either, so every other number in the report is silent about it.
        if (report.LostCoverage)
        {
            Console.Error.WriteLine(
                "The change left no test reaching " +
                string.Join(", ", report.CoverageLost.Select(Path.GetFileNameWithoutExtension)) +
                ", so nothing in it could be judged. Cover it again, exclude it, or declare it test " +
                "support - but a run cannot pass over a component it can no longer ask about.");

            return ExitCode.GateNotPassed;
        }

        if (report.IsInconclusive)
        {
            Console.Error.WriteLine(
                $"{report.Total.ToString(CultureInfo.InvariantCulture)} mutant(s) were generated for " +
                "this change and not one of them could be tested, so the change has not been shown " +
                "to be covered.");

            return ExitCode.GateNotPassed;
        }

        if (report.HasUndetected)
        {
            Console.Error.WriteLine(
                $"{report.Undetected.ToString(CultureInfo.InvariantCulture)} mutant(s) in the " +
                "selected scope were not detected by the tests.");

            return ExitCode.GateNotPassed;
        }

        return ExitCode.Success;
    }

    /// <summary>What a run that was stopped before the end tells the shell.</summary>
    /// <remarks>
    /// <para>
    /// It passes no gate, and it fails only one that was asked for (DEC0013). A gate is a claim about
    /// every mutant in the scope, and a stopped run has judged some; the rule is the one an
    /// undefined score already follows against a threshold (DEC0009) and an incomplete comparison
    /// follows on a partial run (DEC0012): a run that has not established the answer does not report
    /// the one that passes. A partial run is always a gate, so a stopped one always exits 1.
    /// </para>
    /// <para>
    /// With no gate asked for, it exits 0, as every run that only reports does - and says on standard
    /// error that it stopped, so a job that reaches its deadline every night does not do so in
    /// silence. Exiting 2 instead was considered and refused: it means the tool could not work, and a
    /// nightly run that keeps meeting the deadline it was given has not broken anything.
    /// </para>
    /// </remarks>
    private static int StoppedVerdict(RunSettings settings, MutationTestReport report, RunStop stop)
    {
        string reached = report.Stopped?.Selected is { } selected
            ? $"after judging {report.Total.ToString(CultureInfo.InvariantCulture)} of the " +
              $"{selected.ToString(CultureInfo.InvariantCulture)} mutant(s) it selected"
            : "before it had selected a single mutant";

        string undetected = report.Undetected > 0
            ? $" {report.Undetected.ToString(CultureInfo.InvariantCulture)} of those it judged were " +
              "not detected by the tests."
            : string.Empty;

        bool gated = report.Scope.IsPartial || settings.Threshold is not null;

        Console.Error.WriteLine(
            $"The run was stopped by {stop.Reason ?? "a request to stop"} {reached}" +
            (gated
                ? ", so the gate it was asked for cannot be shown to pass."
                : ". No gate was asked for, so it is reported rather than failed.") +
            undetected);

        return gated ? ExitCode.GateNotPassed : ExitCode.Success;
    }

    /// <summary>
    /// Clears the report an earlier run left at <paramref name="reportPath"/>, then creates this
    /// run's verdict log beside it.
    /// </summary>
    /// <remarks>
    /// Cleared when the run starts rather than replaced when it ends, because the end is exactly what
    /// a killed run never reaches. Left in place, an earlier run's finished report would sit beside
    /// this run's partial log, and a step that looks for the report - as slugger's summary step
    /// does - would publish the old verdict as this one's. Cleared, a log with no report beside it
    /// can only be a run that did not finish, which is what DEC0014 says of it.
    /// </remarks>
    internal static VerdictLog OpenVerdictLog(string reportPath)
    {
        // A file only. A directory where the report should go is left for the report's own write to
        // refuse, at the end, with the message that says the measuring itself worked.
        if (File.Exists(reportPath))
        {
            File.Delete(reportPath);
        }

        return VerdictLog.Create(VerdictLog.PathBeside(reportPath));
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
              coverage, breakAt, verifyKills, reportJson, stopAfter - and anything given on the
              command line wins.

            Options:
              -c, --configuration <cfg> Build configuration to analyse and run. Defaults to Release.
              -e, --exclude <pattern>   Leave a project or source file alone. Repeatable. Matched
                                        against the path relative to the directory above, written
                                        with '/'. Note that '*' also matches '/', so 'tests/*'
                                        covers everything beneath 'tests'.
                                        A project can also keep itself out by setting the MSBuild
                                        property KillMutantsTestSupport to true, which says it is
                                        test scaffolding rather than code under test.
              -m, --mutators <list>     Only run these mutator families, comma separated.
                  --without <list>      Leave these families out. Applied after --mutators.
                                        Pass 'none' to run every family even when the file
                                        excludes some.
              -p, --parallel <n>        Mutants to test at once. Defaults to half the processors.
                  --since <revision>    Only judge what changed since a git revision - a branch, a
                                        tag, a commit. The change is measured from the merge base of
                                        that revision and HEAD to the working tree. Such a run
                                        prints findings and a verdict rather than a score, and fails
                                        if the selected scope holds an undetected mutant.
                  --break-at <percent>  Exit with 1 if the mutation score falls below this.
                                        Pass 'none' to clear a breakAt the file sets, which is what
                                        --since needs since it has no score to compare.
                  --no-coverage         Run every test for every mutant, instead of only the ones
                                        that reach it.
                  --report-json <path>  Also write the report as JSON, for CI and tooling. Each
                                        verdict is also appended, as soon as it is final, to a
                                        JSON Lines log beside it - the same path ending in .jsonl
                                        - so a run that is killed still leaves what it judged.
                  --stop-after <time>   Stop testing after this long - 85m, 90s, 1h30m - and report
                                        what was judged. Such a run prints no score and passes no
                                        gate. Ctrl-C does the same; press it twice to quit at once.
                                        Pass 'none' to clear a stopAfter the file sets.
                  --verify-kills <n>    Test n of the mutants reported killed a second time, on
                                        their own, and report any verdict that does not repeat.
                                        Costs one test run each. Defaults to 0.
              -h, --help                Show this help.

            Exit codes:
              0   Ran, and the gate you asked for passed - or you asked for none.
              1   Ran, and the gate did not pass: the score is below --break-at, no mutant could be
                  tested at all, --since found an undetected mutant in the change, or the run was
                  stopped before the gate it was asked for could be shown to pass.
              2   Could not run: see the message on standard error.
              64  The command line was not understood.
            """);
    }
}
