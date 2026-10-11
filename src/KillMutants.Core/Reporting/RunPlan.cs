using System.Security.Cryptography;
using System.Text;

namespace KillMutants.Reporting;

/// <summary>
/// What a run set out to judge, and under what identity: the document written beside the report
/// before the first mutant is tested (DEC0016), and the one a later run of the same series reads to
/// know what it may inherit (DEC0015).
/// </summary>
/// <param name="Format">The shape of this document, so a reader can refuse one it does not know.</param>
/// <param name="Series">The name the caller gave the series, or null for a run that named none.</param>
/// <param name="Identity">What a later run must share with this one to continue it.</param>
/// <param name="Mutants">Every mutant selected, in generation order, in the report's own field names.</param>
/// <param name="Runs">One entry per run of the series, oldest first, the run that wrote this last.</param>
/// <remarks>
/// A plan is written whole and rewritten whole, so that what is on disk is always a document that
/// describes itself. The verdicts themselves are not here: they are the log's, one line each, as
/// DEC0014 settled, and a line joins a row of <see cref="Mutants"/> on its key.
/// </remarks>
public sealed record RunPlan(
    int Format,
    string? Series,
    RunIdentity Identity,
    IReadOnlyList<PlannedMutant> Mutants,
    IReadOnlyList<PlannedRun> Runs)
{
    /// <summary>The format this version of KillMutants writes and reads.</summary>
    public const int CurrentFormat = 1;
}

/// <summary>What two runs must share for the later one to inherit the earlier one's verdicts.</summary>
/// <param name="Code">The state of the code, or null when nothing could read it.</param>
/// <param name="ToolVersion">The KillMutants that ran.</param>
/// <param name="Configuration">The build configuration analysed and run.</param>
/// <param name="Coverage">Whether only the tests reaching a mutant were run against it.</param>
/// <param name="Mutators">The families asked for, or empty for all of them.</param>
/// <param name="Without">The families left out.</param>
/// <param name="Exclude">The patterns left alone.</param>
/// <param name="Scope">What population was inspected, or null before the run has resolved it.</param>
/// <param name="Fingerprint">
/// A digest of every selected mutant's key, or null before the run has selected them.
/// </param>
/// <param name="TargetGraph">
/// Each project under test with the test projects reaching it, or null before discovery.
/// </param>
/// <param name="TestFramework">The xUnit the test applications were built on, or null before the build.</param>
/// <remarks>
/// <para>
/// A mutant's key covers the mutation and nothing else: not the rest of the file, not the tests, not
/// the configuration. Everything here is what a verdict depends on that the key cannot see, and all
/// of it is computed by the run anyway; comparing it costs nothing. See DEC0015.
/// </para>
/// <para>
/// The first group is known before the run starts and decides whether the log is continued or
/// emptied. The last four are known only once the projects are discovered, built and the mutants
/// generated, and are compared then. A field null on either side is one that side did not know yet,
/// and is not compared.
/// </para>
/// </remarks>
public sealed record RunIdentity(
    CodeState? Code,
    string ToolVersion,
    string Configuration,
    bool Coverage,
    IReadOnlyList<string> Mutators,
    IReadOnlyList<string> Without,
    IReadOnlyList<string> Exclude,
    PlannedScope? Scope = null,
    string? Fingerprint = null,
    IReadOnlyList<string>? TargetGraph = null,
    string? TestFramework = null)
{
    /// <summary>
    /// Why this run cannot continue the one that recorded <paramref name="earlier"/>, phrased for a
    /// message, or null when it can.
    /// </summary>
    /// <remarks>
    /// The code state comes first and is the strictest: a tree with uncommitted changes, on either
    /// side, is a tree nothing can prove identical to anything, so a series lives on commits only.
    /// Runtime, operating system and processor count are deliberately absent - they are recorded for
    /// every run and compared by nobody, because a runner image that rolls forward between two nights
    /// of a pinned series would otherwise leave it stuck under a name nobody can change by committing.
    /// </remarks>
    public string? WhyNotContinuedBy(RunIdentity earlier)
    {
        ArgumentNullException.ThrowIfNull(earlier);

        if (earlier.Code is null)
        {
            return "the earlier run did not record the state of its code";
        }

        if (Code is null)
        {
            return "the state of the code cannot be read here";
        }

        if (!earlier.Code.IsExact)
        {
            return $"the earlier run measured a tree with uncommitted changes based on {earlier.Code.Name}";
        }

        if (!Code.IsExact)
        {
            return $"this tree has uncommitted changes based on {Code.Name}";
        }

        if (!string.Equals(Code.Name, earlier.Code.Name, StringComparison.Ordinal))
        {
            return $"the earlier run measured {earlier.Code.Name} and this one {Code.Name}";
        }

        if (!string.Equals(ToolVersion, earlier.ToolVersion, StringComparison.Ordinal))
        {
            return $"the earlier run was KillMutants {earlier.ToolVersion} and this one is {ToolVersion}";
        }

        if (!string.Equals(Configuration, earlier.Configuration, StringComparison.Ordinal))
        {
            return $"the earlier run built {earlier.Configuration} and this one builds {Configuration}";
        }

        if (Coverage != earlier.Coverage)
        {
            return earlier.Coverage
                ? "the earlier run measured coverage and this one does not"
                : "the earlier run did not measure coverage and this one does";
        }

        if (!SameSet(Mutators, earlier.Mutators))
        {
            return "the mutator families differ";
        }

        if (!SameSet(Without, earlier.Without))
        {
            return "the families left out differ";
        }

        if (!SameSet(Exclude, earlier.Exclude))
        {
            return "the exclusions differ";
        }

        if (Scope is not null && earlier.Scope is not null && Scope != earlier.Scope)
        {
            return "the scope differs";
        }

        if (Fingerprint is not null && earlier.Fingerprint is not null &&
            !string.Equals(Fingerprint, earlier.Fingerprint, StringComparison.Ordinal))
        {
            return "the mutants generated here are not the ones the earlier run selected";
        }

        if (TargetGraph is not null && earlier.TargetGraph is not null &&
            !TargetGraph.SequenceEqual(earlier.TargetGraph, StringComparer.Ordinal))
        {
            return "the projects, or the test suites reaching them, differ";
        }

        if (TestFramework is not null && earlier.TestFramework is not null &&
            !string.Equals(TestFramework, earlier.TestFramework, StringComparison.Ordinal))
        {
            return $"the tests were built on {earlier.TestFramework} and are now built on {TestFramework}";
        }

        return null;
    }

    /// <summary>Digests a population by its keys, in a form two machines agree on.</summary>
    /// <remarks>
    /// Over the sorted set of keys rather than the generated order: the order files are walked in
    /// follows the compiler command line, which nothing pins across machines, while the keys do not
    /// depend on it. A key that appears twice counts once - the join between a logged verdict and a
    /// mutant is by key, so two mutants that share one are a population a reader cannot tell apart.
    /// </remarks>
    public static string FingerprintOf(IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        string material = string.Join('\n', keys.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    private static bool SameSet(IReadOnlyList<string> left, IReadOnlyList<string> right) =>
        left.ToHashSet(StringComparer.Ordinal).SetEquals(right);
}

/// <summary>What population a run inspected, as the report records it.</summary>
/// <param name="Mode"><c>full</c> or <c>partial</c>.</param>
/// <param name="ComparedFrom">What a partial run measured the change from, or null.</param>
/// <param name="ComparedTo">The state a partial run measured, or null.</param>
/// <param name="ComparedToIsExact">Whether what was built is exactly that state.</param>
public sealed record PlannedScope(
    string Mode,
    string? ComparedFrom,
    string? ComparedTo,
    bool ComparedToIsExact);

/// <summary>One selected mutant, in the field names the report and the log give it.</summary>
/// <param name="Key">The stable identity a log line joins on.</param>
/// <param name="Id">The counter this run gave it, for reading progress lines.</param>
/// <param name="Mutator">The family that proposed it.</param>
/// <param name="Path">The source file, relative to the directory the run was pointed at.</param>
/// <param name="Line">One-based line.</param>
/// <param name="Character">One-based character within the line.</param>
/// <param name="Original">The expression as written.</param>
/// <param name="Mutated">The expression put in its place.</param>
public sealed record PlannedMutant(
    string Key,
    string Id,
    string Mutator,
    string Path,
    int Line,
    int Character,
    string Original,
    string Mutated);

/// <summary>One run of a series: what it ran on, and what it reached.</summary>
/// <param name="Started">When it started, UTC, ISO 8601.</param>
/// <param name="ToolVersion">The KillMutants it was.</param>
/// <param name="Runtime">The .NET runtime that hosted it.</param>
/// <param name="OperatingSystem">The operating system it ran on.</param>
/// <param name="ProcessorCount">Logical processors the machine offered.</param>
/// <param name="WorkerCount">How many mutants it tested at once.</param>
/// <param name="Verdicts">How many verdicts it reached itself, inherited ones excluded.</param>
/// <param name="KillsReVerified">How many kills it tested a second time, alone.</param>
/// <param name="Disagreements">The verdicts whose re-test did not agree, so the series keeps them.</param>
/// <remarks>
/// Runtime, operating system and processors are recorded and never compared - see
/// <see cref="RunIdentity"/>. Disagreements live here because the log holds verdicts and nothing
/// else, and a disagreement is a qualification of one, recorded without being announced.
/// </remarks>
public sealed record PlannedRun(
    string Started,
    string ToolVersion,
    string Runtime,
    string OperatingSystem,
    int ProcessorCount,
    int WorkerCount,
    int Verdicts,
    int KillsReVerified,
    IReadOnlyList<PlannedDisagreement> Disagreements);

/// <summary>A verdict a re-test did not reproduce, by the key of its mutant.</summary>
/// <param name="Key">The mutant's stable identity.</param>
/// <param name="Disagreement">What was reported, and then what the re-test found.</param>
public sealed record PlannedDisagreement(string Key, string Disagreement);
