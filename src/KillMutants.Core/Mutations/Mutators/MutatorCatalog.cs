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
        new NegatedDecisionMutator(),
        new PatternMutator(),
        new StringLiteralMutator(),
        new StatementMutator(),
    ]);

    /// <summary>
    /// The rules a run applies only when it names them: the families whose signal is real but whose
    /// noise would cost every project that did not ask for them.
    /// </summary>
    /// <remarks>
    /// Named rather than levelled. Stryker.NET groups its optional mutators by level, so raising it
    /// turns several on at once; with one optional family here, a level would be a name for a list
    /// of one. DEC0019.
    /// </remarks>
    private static readonly IReadOnlyList<IMutator> OptIn =
    [
        new NumericLiteralMutator(),
        new LinqMutator(),
    ];

    private static readonly IReadOnlyList<IMutator> Every = [.. Default.Mutators, .. OptIn];

    /// <summary>The rules in this catalog.</summary>
    public IReadOnlyList<IMutator> Mutators { get; }

    /// <summary>Builds a catalogue from rules supplied directly, for tests that need a deliberate one.</summary>
    /// <remarks>
    /// Every shipped mutator refuses a replacement that would not compile, which is the right
    /// behaviour and makes an unbuildable mutant hard to come by on purpose. A test that needs one -
    /// to prove what the run does with it - has to bring its own rule.
    /// </remarks>
    internal static MutatorCatalog Of(IReadOnlyList<IMutator> mutators) => new(mutators);

    /// <summary>
    /// Every family this tool knows, named: the default catalog's in its order, then the ones a run
    /// must ask for.
    /// </summary>
    public static IReadOnlyList<MutatorName> Names { get; } =
        [.. Every.Select(mutator => mutator.Name)];

    /// <summary>The families a run applies only when it names them - see <see cref="OptIn"/>.</summary>
    public static IReadOnlyList<MutatorName> OptInNames { get; } =
        [.. OptIn.Select(mutator => mutator.Name)];

    /// <summary>
    /// The families named in <paramref name="wanted"/>, or the default catalog when none are, with
    /// <paramref name="added"/> joined to them and <paramref name="unwanted"/> left out.
    /// </summary>
    /// <param name="wanted">
    /// The only families to run, or none to start from the default catalog. An opt-in family may be
    /// named here, which runs it alone.
    /// </param>
    /// <param name="unwanted">Families to drop, applied after the other two.</param>
    /// <param name="added">
    /// Families to run on top of the others - what turns an opt-in family on without retyping the
    /// default catalog. A family already there is not added twice.
    /// </param>
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
        IEnumerable<MutatorName>? unwanted = null,
        IEnumerable<MutatorName>? added = null)
    {
        MutatorName[] included = [.. wanted ?? []];
        MutatorName[] excluded = [.. unwanted ?? []];
        MutatorName[] joined = [.. added ?? []];

        RejectUnknown([.. included, .. excluded, .. joined]);

        return new MutatorCatalog(
        [
            .. Every
                .Where(mutator => (included.Length == 0
                                      ? Default.Mutators.Contains(mutator)
                                      : included.Contains(mutator.Name)) ||
                                  joined.Contains(mutator.Name))
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
