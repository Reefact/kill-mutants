using System.Text.Json;
using System.Text.Json.Serialization;

namespace KillMutants.Cli;

/// <summary>The settings a project keeps in <c>killmutants.json</c>, beside its code.</summary>
/// <param name="Configuration">Build configuration to analyse and run.</param>
/// <param name="Exclude">Patterns for projects and source files to leave alone.</param>
/// <param name="Mutators">The only mutator families to run.</param>
/// <param name="Without">Families to leave out.</param>
/// <param name="Parallel">How many mutants to test at once.</param>
/// <param name="Coverage">Whether to run only the tests that reach each mutant.</param>
/// <param name="BreakAt">The mutation score the run must reach, as a percentage.</param>
/// <param name="ReportJson">Where to write the machine-readable report, relative to this file.</param>
/// <remarks>
/// <para>
/// Every setting is optional and every one mirrors a command-line option, because the file exists to
/// stop a project retyping the same flags in every CI job. A run's catalogue in particular is worth
/// versioning with the code: a mutation score only means something against the families that
/// produced it, so a job that picks its catalogue on the command line has put a number nobody can
/// reproduce into its logs.
/// </para>
/// <para>
/// Anything given on the command line wins, so the file states a project's habits and the command
/// line states the exception.
/// </para>
/// </remarks>
internal sealed record ConfigurationFile(
    string? Configuration = null,
    IReadOnlyList<string>? Exclude = null,
    IReadOnlyList<string>? Mutators = null,
    IReadOnlyList<string>? Without = null,
    int? Parallel = null,
    bool? Coverage = null,
    double? BreakAt = null,
    string? ReportJson = null)
{
    /// <summary>The file KillMutants looks for in the directory it was pointed at.</summary>
    public const string Name = "killmutants.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,

        // Written by hand, so it is read the way a person writes: comments explaining why a family
        // is excluded, and a trailing comma left behind by an edit.
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,

        // A misspelt key must not be ignored. Silently running with settings nobody asked for is the
        // same failure the command line refuses a misspelt mutator family for.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Where the file was read from, so paths in it can be resolved against it.</summary>
    public string Directory { get; init; } = string.Empty;

    /// <summary>Reads <c>killmutants.json</c> from <paramref name="directory"/>, if it is there.</summary>
    /// <returns>The settings, or null when the project keeps none.</returns>
    /// <exception cref="ArgumentException">The file exists but could not be understood.</exception>
    public static ConfigurationFile? LoadFrom(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        string path = Path.Combine(directory, Name);

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return (JsonSerializer.Deserialize<ConfigurationFile>(File.ReadAllText(path), Options)
                    ?? new ConfigurationFile())
                with { Directory = directory };
        }
        catch (JsonException exception)
        {
            throw new ArgumentException($"'{path}' could not be read: {exception.Message}", exception);
        }
        catch (IOException exception)
        {
            throw new ArgumentException($"'{path}' could not be read: {exception.Message}", exception);
        }
    }
}
