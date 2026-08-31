namespace KillMutants.Mutations.Mutators;

/// <summary>The set of mutation rules a run applies.</summary>
internal sealed class MutatorCatalog
{
    private MutatorCatalog(IReadOnlyList<IMutator> mutators) => Mutators = mutators;

    /// <summary>
    /// The rules enabled by default: the families that carry the most signal per unit of run time.
    /// </summary>
    /// <remarks>
    /// Every mutant costs a full test run, so the catalogue is grown deliberately rather than
    /// exhaustively. Each family here is covered by its own tests, including an assertion that the
    /// replacement carries the correct syntax kind - see the remarks on <see cref="IMutator"/>.
    /// </remarks>
    public static MutatorCatalog Default { get; } = new(
    [
        new ComparisonOperatorMutator(),
        new LogicalOperatorMutator(),
        new BooleanLiteralMutator(),
        new NegationMutator(),
    ]);

    /// <summary>The rules in this catalog.</summary>
    public IReadOnlyList<IMutator> Mutators { get; }
}
