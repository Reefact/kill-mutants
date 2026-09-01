namespace KillMutants.Cli;

/// <summary>How this run was invoked.</summary>
/// <param name="Directory">Where to look for projects.</param>
/// <param name="Configuration">The build configuration to analyse and run.</param>
/// <param name="WorkerCount">How many mutants to test at once, or null for the default.</param>
internal sealed record CommandLineOptions(string Directory, string Configuration, int? WorkerCount)
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
        string configuration = "Release";
        int? workerCount = null;

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
            workerCount);
    }
}
