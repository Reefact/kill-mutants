namespace KillMutants.Mutations.Mutators;

/// <summary>The set of mutation rules a run applies.</summary>
internal sealed class MutatorCatalog
{
    private MutatorCatalog(IReadOnlyList<IMutator> mutators) => Mutators = mutators;

    /// <summary>
    /// The rules enabled by default. Milestone 1 deliberately ships exactly one: a single mutation
    /// proven to work end to end is worth more than a catalog resting on an unproven engine.
    /// </summary>
    public static MutatorCatalog Default { get; } = new([new GreaterThanOrEqualMutator()]);

    /// <summary>The rules in this catalog.</summary>
    public IReadOnlyList<IMutator> Mutators { get; }
}
