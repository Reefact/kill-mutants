using KillMutants.Mutations;

namespace KillMutants.Reporting;

/// <summary>The outcome of a whole mutation test run.</summary>
public sealed class MutationTestReport
{
    /// <summary>Creates a report from the results of every mutant tested.</summary>
    public MutationTestReport(IReadOnlyList<MutantResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        Results = results;
        Killed = Count(MutantStatus.Killed);
        Survived = Count(MutantStatus.Survived);
        TimedOut = Count(MutantStatus.Timeout);
        CompileErrors = Count(MutantStatus.CompileError);
        Score = MutationScore.FromCounts(Killed, Survived, TimedOut);

        int Count(MutantStatus status) => results.Count(result => result.Status == status);
    }

    /// <summary>Every mutant, with its outcome.</summary>
    public IReadOnlyList<MutantResult> Results { get; }

    /// <summary>How many mutants were generated.</summary>
    public int Total => Results.Count;

    /// <summary>Mutants the tests caught.</summary>
    public int Killed { get; }

    /// <summary>Mutants the tests missed.</summary>
    public int Survived { get; }

    /// <summary>Mutants whose test run exceeded its time budget.</summary>
    public int TimedOut { get; }

    /// <summary>Mutants that did not compile and so could not be tested.</summary>
    public int CompileErrors { get; }

    /// <summary>The share of tested mutants that were caught.</summary>
    public MutationScore Score { get; }

    /// <summary>True when every tested mutant was caught.</summary>
    public bool AllMutantsKilled => Survived == 0;
}
