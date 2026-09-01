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
/// <param name="JsonReportPath">Where to write the machine-readable report, or null for none.</param>
/// <param name="Threshold">The score the run must reach, or null when the run only reports.</param>
internal sealed record RunSettings(
    string Directory,
    string Configuration,
    int? WorkerCount,
    bool MeasureCoverage,
    IReadOnlyList<string> Exclude,
    IReadOnlyList<MutatorName> Mutators,
    IReadOnlyList<MutatorName> WithoutMutators,
    string? JsonReportPath,
    double? Threshold)
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
    /// <exception cref="ArgumentException">A named mutator family does not exist.</exception>
    public static RunSettings From(CommandLineOptions options, ConfigurationFile? file)
    {
        ArgumentNullException.ThrowIfNull(options);

        IReadOnlyList<MutatorName> mutators = Families(options.Mutators, file?.Mutators);
        IReadOnlyList<MutatorName> without = Families(options.WithoutMutators, file?.Without);

        RejectUnknown([.. mutators, .. without]);

        return new RunSettings(
            options.Directory,
            options.Configuration ?? file?.Configuration ?? "Release",
            options.WorkerCount ?? file?.Parallel,
            options.MeasureCoverage ?? file?.Coverage ?? true,
            options.Exclude.Count > 0 ? options.Exclude : file?.Exclude ?? [],
            mutators,
            without,
            options.JsonReportPath ?? Resolve(file?.ReportJson, file?.Directory),
            options.Threshold ?? file?.BreakAt);
    }

    private static IReadOnlyList<MutatorName> Families(
        IReadOnlyList<MutatorName> given,
        IReadOnlyList<string>? configured) =>
        given.Count > 0 ? given : [.. (configured ?? []).Select(MutatorName.Create)];

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
