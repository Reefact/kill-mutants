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
        new ArithmeticOperatorMutator(),
        new BitwiseOperatorMutator(),
        new AssignmentOperatorMutator(),
        new IncrementMutator(),
        new ConditionalExpressionMutator(),
        new NullCoalescingMutator(),
        new BooleanLiteralMutator(),
        new NegationMutator(),
        new StringLiteralMutator(),
    ]);

    /// <summary>The rules in this catalog.</summary>
    public IReadOnlyList<IMutator> Mutators { get; }

    /// <summary>Every family this tool knows, named, in the order the default catalog lists them.</summary>
    public static IReadOnlyList<MutatorName> Names { get; } =
        [.. Default.Mutators.Select(mutator => mutator.Name)];

    /// <summary>
    /// The default catalog narrowed to <paramref name="wanted"/>, or with <paramref name="unwanted"/>
    /// left out.
    /// </summary>
    /// <param name="wanted">The only families to run, or none to start from all of them.</param>
    /// <param name="unwanted">Families to drop, applied after <paramref name="wanted"/>.</param>
    /// <remarks>
    /// A choice worth offering because the families do not carry equal signal, and the difference is
    /// measurable rather than a matter of taste. Run against this repository: the operator families
    /// kill between 45% and 55% of their mutants, while <c>StringLiteral</c> and <c>BooleanLiteral</c>
    /// together account for half the mutants generated and kill 10% to 15% of them - error messages
    /// and flags that nothing asserts on. Those are true findings, and on some projects valuable
    /// ones, which is why this is a switch rather than a deletion.
    /// </remarks>
    /// <exception cref="ArgumentException">A name does not belong to any family.</exception>
    public static MutatorCatalog Of(
        IEnumerable<MutatorName>? wanted = null,
        IEnumerable<MutatorName>? unwanted = null)
    {
        MutatorName[] included = [.. wanted ?? []];
        MutatorName[] excluded = [.. unwanted ?? []];

        RejectUnknown([.. included, .. excluded]);

        return new MutatorCatalog(
        [
            .. Default.Mutators
                .Where(mutator => included.Length == 0 || included.Contains(mutator.Name))
                .Where(mutator => !excluded.Contains(mutator.Name)),
        ]);
    }

    private static void RejectUnknown(IEnumerable<MutatorName> names)
    {
        MutatorName[] unknown = [.. names.Where(name => !Names.Contains(name)).Distinct()];

        if (unknown.Length == 0)
        {
            return;
        }

        throw new ArgumentException(
            $"No mutator is called {string.Join(", ", unknown.Select(name => $"'{name}'"))}. " +
            $"The families are: {string.Join(", ", Names)}.",
            nameof(names));
    }
}
