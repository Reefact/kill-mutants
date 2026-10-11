using System.Globalization;
using KillMutants.Mutations;

namespace KillMutants.Cli;

/// <summary>What a run will actually do, once the command line and the file have both been read.</summary>
/// <param name="Directory">Where to look for projects.</param>
/// <param name="Configuration">The build configuration to analyse and run.</param>
/// <param name="WorkerCount">How many mutants to test at once, or null for the default.</param>
/// <param name="MeasureCoverage">Run only the tests that reach each mutant.</param>
/// <param name="Exclude">Patterns for projects and source files to leave alone.</param>
/// <param name="Mutators">The only mutator families to run, or empty for all of them.</param>
/// <param name="WithoutMutators">Families to leave out, applied after <paramref name="Mutators"/>.</param>
/// <param name="VerifyKills">How many of the mutants reported killed to test a second time.</param>
/// <param name="JsonReportPath">Where to write the machine-readable report, or null for none.</param>
/// <param name="Threshold">The score the run must reach, or null when the run only reports.</param>
/// <param name="Since">The revision to measure a change from, or null for the whole codebase.</param>
/// <param name="StopAfter">
/// How long the run may take before it stops and reports what it judged, or null for no limit.
/// </param>
/// <param name="Resume">
/// The series this run continues, or starts when none of that name exists beside the report; null
/// for a run that names none. See DEC0015.
/// </param>
internal sealed record RunSettings(
    string Directory,
    string Configuration,
    int? WorkerCount,
    bool MeasureCoverage,
    IReadOnlyList<string> Exclude,
    IReadOnlyList<MutatorName> Mutators,
    IReadOnlyList<MutatorName> WithoutMutators,
    int VerifyKills,
    string? JsonReportPath,
    double? Threshold,
    string? Since,
    TimeSpan? StopAfter = null,
    string? Resume = null)
{
    /// <summary>Resolves what was asked against what the project keeps in its file.</summary>
    /// <param name="options">What the command line asked for.</param>
    /// <param name="file">The project's settings, or null when it keeps none.</param>
    /// <remarks>
    /// <para>
    /// One rule, in one direction: anything given on the command line wins, otherwise the file, and
    /// otherwise the default. So the file states a project's habits and the command line states the
    /// exception - which is why <see cref="CommandLineOptions"/> keeps every setting nullable rather
    /// than baking the defaults in, where they would silently outrank a file they never mentioned.
    /// </para>
    /// <para>
    /// A list given on the command line <em>replaces</em> the file's rather than adding to it. The
    /// alternative reads well until someone needs to run without an exclusion the file states, and
    /// finds there is no way to say so.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// A named mutator family does not exist, or a value in the file is one the command line would
    /// have refused.
    /// </exception>
    public static RunSettings From(CommandLineOptions options, ConfigurationFile? file)
    {
        ArgumentNullException.ThrowIfNull(options);

        IReadOnlyList<MutatorName> mutators = Families(options.Mutators, file?.Mutators);
        IReadOnlyList<MutatorName> without = Families(options.WithoutMutators, file?.Without);

        RejectUnknown([.. mutators, .. without]);
        RejectImpossible(file);
        RejectThresholdWithSince(options, file);

        string? jsonReportPath = options.JsonReportPath ?? Resolve(file?.ReportJson, file?.Directory);

        RejectReportNamedLikeItsLog(jsonReportPath, fromFile: options.JsonReportPath is null);
        RejectResumeWithoutItsFiles(options, jsonReportPath);

        return new RunSettings(
            options.Directory,
            options.Configuration ?? file?.Configuration ?? "Release",
            options.WorkerCount ?? file?.Parallel,
            options.MeasureCoverage ?? file?.Coverage ?? true,
            options.Exclude.Count > 0 ? options.Exclude : file?.Exclude ?? [],
            mutators,
            without,
            options.VerifyKills ?? file?.VerifyKills ?? 0,
            jsonReportPath,
            options.ThresholdCleared ? null : options.Threshold ?? file?.BreakAt,
            options.Since,
            options.StopAfterCleared ? null : options.StopAfter ?? StopAfterIn(file),
            options.Resume);
    }

    /// <summary>
    /// Refuses a series that would have nowhere to live, or would measure a population a series
    /// cannot yet describe.
    /// </summary>
    /// <remarks>
    /// A series is the plan and the log beside the report, so without a report there is nothing to
    /// continue from. And a partial run's identity would be the two revisions it compares rather
    /// than one commit, which DEC0015 leaves to a record of its own; refusing now is what keeps a
    /// change-scoped series from being started with an identity nobody has thought through.
    /// </remarks>
    private static void RejectResumeWithoutItsFiles(CommandLineOptions options, string? reportPath)
    {
        if (options.Resume is null)
        {
            return;
        }

        if (reportPath is null)
        {
            throw new ArgumentException(
                "'--resume' needs '--report-json' (or 'reportJson' in killmutants.json): a series " +
                "lives in the plan and the verdict log written beside the report.");
        }

        if (options.Since is not null)
        {
            throw new ArgumentException(
                "'--resume' cannot be combined with '--since': a series measures the whole codebase at " +
                "one commit, and what a change-scoped series would be is not decided yet.");
        }
    }

    /// <summary>The deadline the file sets, already known to be readable.</summary>
    private static TimeSpan? StopAfterIn(ConfigurationFile? file) =>
        file?.StopAfter is { } text && Duration.TryParse(text, out TimeSpan deadline) ? deadline : null;

    /// <summary>
    /// Refuses a report path that would share its name with the verdict log written beside it.
    /// </summary>
    /// <remarks>
    /// The log takes the report's name with <c>.jsonl</c> in place of its extension (DEC0014), so a
    /// report already named that way would be the log: written line by line as the run goes, then
    /// replaced by the report at the end. A job reading it after a cancelled run would find JSON
    /// Lines where it expected the report, and after a completed one would find the log gone.
    /// </remarks>
    private static void RejectReportNamedLikeItsLog(string? reportPath, bool fromFile)
    {
        if (reportPath is null)
        {
            return;
        }

        string where = fromFile ? $"'reportJson' in {ConfigurationFile.Name}" : "'--report-json'";

        if (string.Equals(Path.GetExtension(reportPath), ".jsonl", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"The JSON report cannot be written to '{reportPath}' ({where}): a name ending in " +
                ".jsonl is the one the verdict log takes beside the report. Give the report another " +
                "extension, such as .json.");
        }

        // The plan's name, for the same reason (DEC0016): a report named like it would be rewritten
        // as the plan during the run and replaced by the report at the end.
        if (reportPath.EndsWith(".plan.json", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"The JSON report cannot be written to '{reportPath}' ({where}): a name ending in " +
                ".plan.json is the one the plan takes beside the report. Give the report another " +
                "name, such as report.json.");
        }
    }

    /// <summary>
    /// Refuses a threshold on a partial run, and names where the threshold came from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A threshold means a score, and a partial run has none - see DEC0010. Refusing is the only
    /// honest option: silently ignoring the threshold would leave a job that believes it has a
    /// quality gate with none at all, which is the failure that document exists to prevent.
    /// </para>
    /// <para>
    /// Naming the file is not a nicety. <c>breakAt</c> in <c>killmutants.json</c> is exactly what the
    /// README tells a project to keep there, so without this the message would send a reader to a
    /// command line that does not mention a threshold, and every partial run in that repository
    /// would be refused with no way out short of editing a versioned file. The way out is
    /// <c>--break-at none</c>, which clears it for this run - the same shape as <c>--without none</c>.
    /// </para>
    /// </remarks>
    private static void RejectThresholdWithSince(CommandLineOptions options, ConfigurationFile? file)
    {
        if (options.Since is null || options.ThresholdCleared)
        {
            return;
        }

        if (options.Threshold is not null)
        {
            throw new ArgumentException(
                "'--break-at' cannot be used with '--since': a partial run reports findings and a " +
                "verdict, not a mutation score. Drop the threshold, or drop '--since'.");
        }

        if (file?.BreakAt is not null)
        {
            throw new ArgumentException(
                $"'--since' cannot be used while a threshold is set ('breakAt' in " +
                $"{ConfigurationFile.Name}): a partial run reports findings and a verdict, not a " +
                "mutation score. Pass '--break-at none' to clear it for this run.");
        }
    }

    /// <summary>
    /// Holds the file to the same rules as the command line, because they are the same settings.
    /// </summary>
    /// <remarks>
    /// The command line parses and validates in one step, so the file was the only way in for a
    /// value that cannot mean anything. <c>"parallel": 0</c> was the sharp one: it reached the
    /// session, which built no sandbox and then indexed the first one, so a setting a user could
    /// reasonably try produced an IndexOutOfRangeException and exit code 134 rather than the
    /// documented bad-usage exit.
    /// </remarks>
    private static void RejectImpossible(ConfigurationFile? file)
    {
        if (file is null)
        {
            return;
        }

        // Worded like the command line's own refusals, with the setting named: a value read from a
        // file has to say where it was read from, or the reader looks at their shell history.
        if (file.Parallel is { } parallel && parallel < 1)
        {
            throw new ArgumentException(
                $"'{parallel.ToString(CultureInfo.InvariantCulture)}' is not a positive number of " +
                $"workers ('parallel' in {ConfigurationFile.Name}).");
        }

        if (file.VerifyKills is { } verifyKills && verifyKills < 0)
        {
            throw new ArgumentException(
                $"'{verifyKills.ToString(CultureInfo.InvariantCulture)}' is not a count of mutants " +
                $"to test again, zero or more ('verifyKills' in {ConfigurationFile.Name}).");
        }

        // IsFinite for the same reason as on the command line: every comparison with NaN is false,
        // so a threshold that is not a number passes every range check and then the verdict too.
        if (file.BreakAt is { } breakAt && (!double.IsFinite(breakAt) || breakAt < 0 || breakAt > 100))
        {
            throw new ArgumentException(
                $"'{breakAt.ToString(CultureInfo.InvariantCulture)}' is not a percentage between " +
                $"0 and 100 ('breakAt' in {ConfigurationFile.Name}).");
        }

        // A deadline the file cannot express is refused rather than dropped, or a job that believes
        // it stops in time would run until its CI kills it - the case the option exists to prevent.
        if (file.StopAfter is { } stopAfter && !Duration.TryParse(stopAfter, out _))
        {
            throw new ArgumentException(
                $"'{stopAfter}' is not {Duration.Expected} ('stopAfter' in {ConfigurationFile.Name}).");
        }
    }

    /// <summary>
    /// What was asked for, or what the file says when nothing was asked.
    /// </summary>
    /// <remarks>
    /// Null and empty are not the same thing here, and treating them as one is what left a
    /// <c>without</c> in the file impossible to switch off: any list on the command line replaces
    /// the file's, including a deliberately empty one - <c>--without none</c>.
    /// </remarks>
    private static IReadOnlyList<MutatorName> Families(
        IReadOnlyList<MutatorName>? given,
        IReadOnlyList<string>? configured) =>
        given ?? [.. (configured ?? []).Select(MutatorName.Create)];

    /// <summary>
    /// Resolves a path from the file against the file's own directory rather than the shell's.
    /// </summary>
    /// <remarks>
    /// A path written in a project's file means a place in that project. Resolving it against the
    /// working directory would put the report somewhere different depending on where the run was
    /// started from, which is exactly what keeping the setting in a file is meant to stop.
    /// </remarks>
    private static string? Resolve(string? path, string? directory) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(Path.Combine(directory ?? ".", path));

    /// <summary>
    /// Refuses a family that does not exist, wherever it was named.
    /// </summary>
    /// <remarks>
    /// Checked once for both sources on purpose. A typo must not silently narrow a run: a mutation
    /// score only means something against the families that produced it, so reporting one for a
    /// catalogue nobody asked for is worse than reporting none.
    /// </remarks>
    private static void RejectUnknown(IReadOnlyList<MutatorName> named)
    {
        MutatorName[] unknown = [.. named.Except(MutationTesting.MutatorFamilies).Distinct()];

        if (unknown.Length > 0)
        {
            throw new ArgumentException(
                $"No mutator is called {string.Join(", ", unknown.Select(name => $"'{name}'"))}. " +
                $"The families are: {string.Join(", ", MutationTesting.MutatorFamilies)}.");
        }
    }

    /// <summary>The threshold as it is written in messages.</summary>
    public string ThresholdText =>
        Threshold?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;
}
