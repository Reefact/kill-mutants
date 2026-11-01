using KillMutants.Mutations;

namespace KillMutants.Cli;

/// <summary>What the command line asked for, and only that.</summary>
/// <param name="Directory">Where to look for projects.</param>
/// <param name="Configuration">The build configuration, or null when it was not given.</param>
/// <param name="WorkerCount">How many mutants to test at once, or null when it was not given.</param>
/// <param name="MeasureCoverage">Whether to select tests by coverage, or null when it was not given.</param>
/// <param name="Exclude">Patterns to leave alone. Empty means the option was not given.</param>
/// <param name="Mutators">The only families to run. Empty means the option was not given.</param>
/// <param name="WithoutMutators">Families to leave out. Empty means the option was not given.</param>
/// <param name="VerifyKills">How many kills to test again, or null when it was not given.</param>
/// <param name="JsonReportPath">Where to write the JSON report, or null when it was not given.</param>
/// <param name="Threshold">The score the run must reach, or null when it was not given.</param>
/// <param name="ThresholdCleared">True when <c>--break-at none</c> was given.</param>
/// <param name="Since">The revision to measure a change from, or null for the whole codebase.</param>
/// <param name="StopAfter">How long the run may take before it stops, or null when not given.</param>
/// <param name="StopAfterCleared">True when <c>--stop-after none</c> was given.</param>
/// <param name="Resume">The series this run continues or starts, or null when it names none.</param>
/// <param name="WithMutators">Families to add to the others, or null when the option was not given.</param>
/// <param name="TargetFramework">The framework the test projects run on, or null when it was not given.</param>
/// <param name="IgnoreUntracked">
/// The untracked files a series does not count, or null when the option was not given.
/// </param>
/// <param name="Progress">What the console shows while the run runs, or null when it was not given.</param>
/// <param name="IgnoreCalls">
/// The calls the project does not hear about, or null when the option was not given; empty for
/// <c>--ignore-calls none</c>.
/// </param>
/// <param name="Slow">How long a mutant runs before it is named as slow, or null when not given.</param>
/// <remarks>
/// Every setting is nullable, and that is the point: <c>--configuration Release</c> and saying
/// nothing must be told apart, because a project's <c>killmutants.json</c> sits between them.
/// Baking the defaults in here would let the command line silently override a file it never
/// mentioned. <see cref="RunSettings"/> is what a run actually uses.
/// </remarks>
internal sealed record CommandLineOptions(
    string Directory,
    string? Configuration,
    int? WorkerCount,
    bool? MeasureCoverage,
    IReadOnlyList<string> Exclude,
    IReadOnlyList<MutatorName>? Mutators,
    IReadOnlyList<MutatorName>? WithoutMutators,
    int? VerifyKills,
    string? JsonReportPath,
    double? Threshold,
    bool ThresholdCleared,
    string? Since,
    TimeSpan? StopAfter = null,
    bool StopAfterCleared = false,
    string? Resume = null,
    IReadOnlyList<MutatorName>? WithMutators = null,
    string? TargetFramework = null,
    IReadOnlyList<string>? IgnoreUntracked = null,
    ProgressMode? Progress = null,
    TimeSpan? Slow = null,
    IReadOnlyList<string>? IgnoreCalls = null)
{
    /// <summary>
    /// Parses the command line. The defaults are chosen so that <c>dotnet killmutants</c> with no
    /// arguments does the right thing in the ordinary case.
    /// </summary>
    /// <exception cref="ArgumentException">An option was malformed.</exception>
    public static CommandLineOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? directory = null;
        string? configuration = null;
        int? workerCount = null;
        bool? measureCoverage = null;
        List<string> exclude = [];
        // Null until the option appears. An omitted list and a list given as empty mean different
        // things - "use whatever the file says" and "nothing at all" - and collapsing them is what
        // made a `without` in killmutants.json impossible to switch off.
        IReadOnlyList<MutatorName>? mutators = null;
        IReadOnlyList<MutatorName>? withoutMutators = null;
        IReadOnlyList<MutatorName>? withMutators = null;
        int? verifyKills = null;
        string? jsonReportPath = null;
        double? threshold = null;
        // Distinct from a null threshold, exactly as an empty family list is distinct from an absent
        // one: a project that keeps breakAt in killmutants.json needs a way to say "not this run",
        // and --since refuses a threshold from either source.
        bool thresholdCleared = false;
        string? since = null;
        TimeSpan? stopAfter = null;
        bool stopAfterCleared = false;
        string? resume = null;
        string? targetFramework = null;
        List<string>? ignoreUntracked = null;
        ProgressMode? progress = null;
        TimeSpan? slow = null;
        List<string>? ignoreCalls = null;

        for (int index = 0; index < args.Count; index++)
        {
            string argument = args[index];

            switch (argument)
            {
                case "-c" or "--configuration":
                    index++;

                    if (index >= args.Count)
                    {
                        throw new ArgumentException($"'{argument}' needs a configuration name.");
                    }

                    configuration = args[index];

                    break;

                case "--target-framework":
                    index++;

                    if (index >= args.Count || string.IsNullOrWhiteSpace(args[index]))
                    {
                        throw new ArgumentException($"'{argument}' needs a target framework, such as net10.0.");
                    }

                    targetFramework = args[index].Trim();

                    break;

                case "-p" or "--parallel":
                    index++;

                    if (index >= args.Count)
                    {
                        throw new ArgumentException($"'{argument}' needs a number of workers.");
                    }

                    if (!int.TryParse(args[index], System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out int parsed) || parsed < 1)
                    {
                        throw new ArgumentException($"'{args[index]}' is not a positive number of workers.");
                    }

                    workerCount = parsed;

                    break;

                case "--no-coverage":
                    measureCoverage = false;

                    break;

                case "-e" or "--exclude":
                    index++;

                    if (index >= args.Count || string.IsNullOrWhiteSpace(args[index]))
                    {
                        throw new ArgumentException($"'{argument}' needs a path pattern.");
                    }

                    exclude.Add(args[index]);

                    break;

                case "--ignore-untracked":
                    index++;

                    // Only the shape here. What a pattern may cover is settled in RunSettings, so the
                    // file and the command line are refused by one rule with one message (DEC0025).
                    if (index >= args.Count || string.IsNullOrWhiteSpace(args[index]))
                    {
                        throw new ArgumentException(
                            $"'{argument}' needs a pattern naming the untracked files the tests write, " +
                            "such as '*.received.html'.");
                    }

                    (ignoreUntracked ??= []).Add(args[index]);

                    break;

                case "-m" or "--mutators":
                    index++;
                    mutators = [.. mutators ?? [], .. Families(argument, index < args.Count ? args[index] : null)];

                    break;

                case "--with":
                    index++;
                    withMutators = [.. withMutators ?? [], .. Families(argument, index < args.Count ? args[index] : null)];

                    break;

                case "--without":
                    index++;
                    withoutMutators = [.. withoutMutators ?? [], .. Families(argument, index < args.Count ? args[index] : null)];

                    break;

                case "--verify-kills":
                    index++;

                    if (index >= args.Count ||
                        !int.TryParse(
                            args[index], System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out int sample) ||
                        sample < 0)
                    {
                        throw new ArgumentException(
                            $"'{argument}' needs a count of mutants to test again, zero or more.");
                    }

                    verifyKills = sample;

                    break;

                case "--since":
                    index++;

                    if (index >= args.Count || string.IsNullOrWhiteSpace(args[index]))
                    {
                        throw new ArgumentException(
                            $"'{argument}' needs a git revision - a branch, a tag or a commit.");
                    }

                    since = args[index];

                    break;

                case "--break-at":
                    index++;

                    if (index >= args.Count)
                    {
                        throw new ArgumentException($"'{argument}' needs a percentage.");
                    }

                    if (string.Equals(args[index], EmptyList, StringComparison.OrdinalIgnoreCase))
                    {
                        thresholdCleared = true;
                        threshold = null;

                        break;
                    }

                    // Cleared by a later value, or the option stops being last-one-wins: review
                    // found that '--break-at none --break-at 80' kept the marker and resolved to no
                    // threshold at all, so a job that had asked for a gate twice would have had
                    // none - and would have been allowed to combine it with --since besides. A
                    // quality gate that disarms itself silently is the one failure this must not
                    // have.
                    thresholdCleared = false;

                    // IsFinite first, and not as an afterthought: TryParse accepts "NaN", and every
                    // comparison with NaN is false - including the range check here and the one the
                    // verdict makes at the end. A threshold of NaN therefore passes every gate and
                    // disarms the build break silently, which is the one thing a quality gate must
                    // never do.
                    if (!double.TryParse(
                            args[index], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double percentage) ||
                        !double.IsFinite(percentage) ||
                        percentage < 0 || percentage > 100)
                    {
                        throw new ArgumentException(
                            $"'{args[index]}' is not a percentage between 0 and 100.");
                    }

                    threshold = percentage;

                    break;

                case "--ignore-calls":
                    index++;

                    if (index >= args.Count || string.IsNullOrWhiteSpace(args[index]))
                    {
                        throw new ArgumentException(
                            $"'{argument}' needs a pattern naming a method or constructor, such as " +
                            $"'System.ArgumentNullException.ThrowIfNull' or '*.ConfigureAwait', or '{EmptyList}'.");
                    }

                    // 'none' empties the list and replaces the file's, as it does for a family list;
                    // a pattern after it fills the list again. What a pattern may be is settled in
                    // RunSettings, so the file and the command line are refused by one rule.
                    if (string.Equals(args[index], EmptyList, StringComparison.OrdinalIgnoreCase))
                    {
                        ignoreCalls = [];
                    }
                    else
                    {
                        (ignoreCalls ??= []).Add(args[index]);
                    }

                    break;

                case "--progress":
                    index++;
                    progress = ProgressModeOf(index < args.Count ? args[index] : null)
                        ?? throw new ArgumentException($"'{argument}' needs {ProgressModes}.");

                    break;

                case "--slow":
                    index++;

                    if (index >= args.Count || !Duration.TryParse(args[index], out TimeSpan after) || after <= TimeSpan.Zero)
                    {
                        throw new ArgumentException($"'{argument}' needs {Duration.Expected}, longer than nothing.");
                    }

                    slow = after;

                    break;

                case "--stop-after":
                    index++;

                    if (index >= args.Count)
                    {
                        throw new ArgumentException($"'{argument}' needs {Duration.Expected}.");
                    }

                    // Last one wins, for the reason --break-at's does: a later value clears an
                    // earlier 'none', or a job that asked for a deadline twice would have none.
                    if (string.Equals(args[index], EmptyList, StringComparison.OrdinalIgnoreCase))
                    {
                        stopAfterCleared = true;
                        stopAfter = null;

                        break;
                    }

                    if (!Duration.TryParse(args[index], out TimeSpan deadline))
                    {
                        throw new ArgumentException($"'{args[index]}' is not {Duration.Expected}.");
                    }

                    stopAfterCleared = false;
                    stopAfter = deadline;

                    break;

                case "--report-json":
                    index++;

                    if (index >= args.Count)
                    {
                        throw new ArgumentException($"'{argument}' needs a file path.");
                    }

                    jsonReportPath = Path.GetFullPath(args[index]);

                    break;

                case "--resume":
                    index++;

                    // A name, not a flag: the caller decides what a series is - the commit hash,
                    // typically - and the tool only checks that what it is asked to continue is
                    // the same code. See DEC0015.
                    if (index >= args.Count || string.IsNullOrWhiteSpace(args[index]) ||
                        args[index].StartsWith('-'))
                    {
                        throw new ArgumentException(
                            $"'{argument}' needs a name for the series - the commit hash, typically.");
                    }

                    resume = args[index];

                    break;

                default:
                    if (argument.StartsWith('-'))
                    {
                        throw new ArgumentException($"Unknown option '{argument}'.");
                    }

                    if (directory is not null)
                    {
                        throw new ArgumentException("Only one directory may be given.");
                    }

                    directory = argument;

                    break;
            }
        }

        return new CommandLineOptions(
            Path.GetFullPath(directory ?? System.IO.Directory.GetCurrentDirectory()),
            configuration,
            workerCount,
            measureCoverage,
            exclude,
            mutators,
            withoutMutators,
            verifyKills,
            jsonReportPath,
            threshold,
            thresholdCleared,
            since,
            stopAfter,
            stopAfterCleared,
            resume,
            withMutators,
            targetFramework,
            ignoreUntracked,
            progress,
            slow,
            ignoreCalls);
    }

    /// <summary>The values <c>--progress</c> takes, for messages refusing one.</summary>
    public const string ProgressModes = "one of log, live or none";

    /// <summary>Reads a progress mode, as the command line and the file write it, or null.</summary>
    /// <remarks>
    /// The three names are the contract DEC0023 holds, so they are matched exactly, ignoring case,
    /// rather than through the enum's own parsing, which would also accept a number.
    /// </remarks>
    public static ProgressMode? ProgressModeOf(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        "log" => ProgressMode.Log,
        "live" => ProgressMode.Live,
        "none" => ProgressMode.None,
        _ => null,
    };

    /// <summary>Parses a comma-separated list of mutator family names.</summary>
    /// <remarks>
    /// Only the shape is checked here. Whether a name is a real family is settled once, in
    /// <see cref="RunSettings"/>, so the same rule and the same message cover the file as well as
    /// the command line.
    /// </remarks>
    /// <exception cref="ArgumentException">The list is missing or empty.</exception>
    /// <summary>The value a list option takes to mean "an empty list, on purpose".</summary>
    /// <remarks>
    /// The documented rule is that a list on the command line replaces the file's, and without this
    /// there was one list nobody could replace: a <c>without</c> in killmutants.json applied to
    /// every run, because an omitted option and an explicitly empty one both arrive here as nothing
    /// and an empty value is refused. Naming every family with <c>--mutators</c> did not help - the
    /// exclusions were still applied afterwards. A word cannot be confused with a family, since no
    /// family is called this.
    /// </remarks>
    public const string EmptyList = "none";

    private static MutatorName[] Families(string option, string? value)
    {
        if (string.Equals(value, EmptyList, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        string[] names = value is null
            ? []
            : [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

        if (names.Length == 0)
        {
            throw new ArgumentException(
                $"'{option}' needs a comma-separated list, or '{EmptyList}' for no families at all. " +
                $"The families are: {string.Join(", ", MutationTesting.MutatorFamilies)}.");
        }

        return [.. names.Select(MutatorName.Create)];
    }
}
