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
/// <param name="JsonReportPath">Where to write the JSON report, or null when it was not given.</param>
/// <param name="Threshold">The score the run must reach, or null when it was not given.</param>
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
    IReadOnlyList<MutatorName> Mutators,
    IReadOnlyList<MutatorName> WithoutMutators,
    string? JsonReportPath,
    double? Threshold)
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
        List<MutatorName> mutators = [];
        List<MutatorName> withoutMutators = [];
        string? jsonReportPath = null;
        double? threshold = null;

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
                    mutators.AddRange(Families(argument, index < args.Count ? args[index] : null));

                    break;

                case "--without":
                    index++;
                    withoutMutators.AddRange(Families(argument, index < args.Count ? args[index] : null));

                    break;

                case "--break-at":
                    index++;

                    if (index >= args.Count)
                    {
                        throw new ArgumentException($"'{argument}' needs a percentage.");
                    }

                    if (!double.TryParse(
                            args[index], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double percentage) ||
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
            jsonReportPath,
            threshold);
    }

    /// <summary>Parses a comma-separated list of mutator family names.</summary>
    /// <remarks>
    /// Only the shape is checked here. Whether a name is a real family is settled once, in
    /// <see cref="RunSettings"/>, so the same rule and the same message cover the file as well as
    /// the command line.
    /// </remarks>
    /// <exception cref="ArgumentException">The list is missing or empty.</exception>
    private static MutatorName[] Families(string option, string? value)
    {
        string[] names = value is null
            ? []
            : [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

        if (names.Length == 0)
        {
            throw new ArgumentException(
                $"'{option}' needs a comma-separated list. " +
                $"The families are: {string.Join(", ", MutationTesting.MutatorFamilies)}.");
        }

        return [.. names.Select(MutatorName.Create)];
    }
}
