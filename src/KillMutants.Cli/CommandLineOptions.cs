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
    string? Since)
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
        int? verifyKills = null;
        string? jsonReportPath = null;
        double? threshold = null;
        // Distinct from a null threshold, exactly as an empty family list is distinct from an absent
        // one: a project that keeps breakAt in killmutants.json needs a way to say "not this run",
        // and --since refuses a threshold from either source.
        bool thresholdCleared = false;
        string? since = null;

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

                case "-m" or "--mutators":
                    index++;
                    mutators = [.. mutators ?? [], .. Families(argument, index < args.Count ? args[index] : null)];

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

                case "--report-json":
                    index++;

                    if (index >= args.Count)
                    {
                        throw new ArgumentException($"'{argument}' needs a file path.");
                    }

                    jsonReportPath = Path.GetFullPath(args[index]);

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
            since);
    }

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
