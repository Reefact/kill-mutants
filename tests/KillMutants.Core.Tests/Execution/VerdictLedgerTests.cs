using KillMutants.Execution;
using KillMutants.Mutations;
using KillMutants.Mutations.Mutators;
using KillMutants.Reporting;

namespace KillMutants.Core.Tests.Execution;

/// <summary>
/// The ledger is the one place a verdict becomes final. A verdict inherited from an earlier run of
/// the series (DEC0015) is final too, counts towards completeness, and is told to nobody - the log
/// already holds its line.
/// </summary>
public class VerdictLedgerTests
{
    private static IReadOnlyList<Mutant> Mutants() =>
        new MutantGenerator(MutatorCatalog.Default)
            .Generate(TestCompilation.From("class C { bool M(int a) => a >= 18 && a < 65; }", "/src/Ages.cs"));

    [Fact]
    public void An_inherited_verdict_is_final_and_told_to_nobody()
    {
        IReadOnlyList<Mutant> mutants = Mutants();
        var heard = new List<MutantResult>();
        var ledger = new VerdictLedger(new Observer(heard));

        ledger.Select(mutants);
        ledger.Inherit(new MutantResult(mutants[0], MutantStatus.Survived));
        ledger.Reach(new MutantResult(mutants[1], MutantStatus.Killed));

        Assert.True(ledger.IsReached(mutants[0]));
        Assert.True(ledger.IsReached(mutants[1]));
        Assert.False(ledger.IsReached(mutants[2]));
        Assert.Equal(MutantStatus.Survived, ledger.VerdictOf(mutants[0])!.Status);
        Assert.Null(ledger.VerdictOf(mutants[2]));
        Assert.Equal(1, ledger.Inherited);
        Assert.Equal(1, ledger.ReachedHere);
        Assert.Equal([mutants[1]], heard.Select(result => result.Mutant));
    }

    [Fact]
    public void A_run_that_inherited_everything_has_reached_everything()
    {
        IReadOnlyList<Mutant> mutants = Mutants();
        var ledger = new VerdictLedger(observer: null);

        ledger.Select(mutants);

        foreach (Mutant mutant in mutants)
        {
            ledger.Inherit(new MutantResult(mutant, MutantStatus.Killed));
        }

        Assert.True(ledger.HasReachedEverySelected);
        Assert.Equal(mutants.Count, ledger.InOrder().Count);
        Assert.Equal(0, ledger.ReachedHere);
    }

    [Fact]
    public void Inherited_and_reached_verdicts_are_reported_in_generation_order()
    {
        IReadOnlyList<Mutant> mutants = Mutants();
        var ledger = new VerdictLedger(observer: null);

        ledger.Select(mutants);
        ledger.Reach(new MutantResult(mutants[^1], MutantStatus.Killed));
        ledger.Inherit(new MutantResult(mutants[0], MutantStatus.Survived));

        Assert.Equal([mutants[0], mutants[^1]], ledger.InOrder().Select(result => result.Mutant));
    }

    private sealed class Observer(List<MutantResult> heard) : IProgress<MutantResult>
    {
        public void Report(MutantResult value) => heard.Add(value);
    }
}
