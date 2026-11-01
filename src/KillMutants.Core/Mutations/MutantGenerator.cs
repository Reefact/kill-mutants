using KillMutants.Filtering;
using KillMutants.Mutations.Mutators;
using KillMutants.Selection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KillMutants.Mutations;

/// <summary>Walks syntax trees and produces the mutants a catalog proposes for them.</summary>
internal sealed class MutantGenerator
{
    private readonly MutatorCatalog _catalog;
    private readonly bool _reachesConstants;
    private readonly PathFilter _exclusions;
    private readonly IgnoredCalls _ignoredCalls;
    private readonly string _root;

    private MutantId _nextId = MutantId.First;

    /// <param name="catalog">The rules to apply.</param>
    /// <param name="exclusions">Paths to leave alone.</param>
    /// <param name="root">
    /// The directory the run was pointed at. Mutants record their file relative to it, so that a
    /// mutant's key is the same on a CI runner and on a laptop - see <see cref="MutantKey"/>.
    /// </param>
    /// <param name="ignoredCalls">The calls the project does not hear about (DEC0026), or none.</param>
    public MutantGenerator(
        MutatorCatalog catalog,
        PathFilter? exclusions = null,
        string? root = null,
        IgnoredCalls? ignoredCalls = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        _catalog = catalog;
        _reachesConstants = catalog.Mutators.Any(mutator => mutator.ReachesConstants);
        _exclusions = exclusions ?? PathFilter.None;
        _ignoredCalls = ignoredCalls ?? IgnoredCalls.None;
        _root = root ?? string.Empty;
    }

    /// <summary>
    /// Produces every mutant the catalog proposes across <paramref name="compilation"/>, skipping
    /// generated sources - see <see cref="SourceFile"/> - and sites whose mutation could never be
    /// observed. Generated files are still <em>compiled</em>: dropping them would change the
    /// assembly's identity.
    /// </summary>
    /// <param name="compilation">The project to walk.</param>
    /// <param name="selection">
    /// The files a partial run is judging, or null for every file. Matched against the syntax tree's
    /// own path, which is the only thing that knows for certain what a project was built from - a
    /// linked file, a glob reaching outside the project directory, a file the project excludes.
    /// </param>
    /// <param name="cancellationToken">
    /// Observed between files, so a run that is stopped while it generates stops within one file's
    /// walk rather than after every project's.
    /// </param>
    /// <remarks>
    /// Identifiers continue across calls, so one generator numbers a whole run. A generator per
    /// project would otherwise restart at <c>M1</c> for each of them and make the report ambiguous.
    /// A partial run numbers only what it generated, and its report says so: see
    /// <see cref="Reporting.RunScope"/>.
    /// </remarks>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public IReadOnlyList<Mutant> Generate(
        Compilation compilation,
        MutantSelection? selection = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        selection ??= MutantSelection.Everything;

        List<Mutant> mutants = [];

        // Built at the first constant a rule proposes to mutate, and never for a catalogue that
        // reaches none - which is the default one.
        Lazy<ILookup<string, IdentifierNameSyntax>> identifiers =
            new(() => MutationSite.IdentifiersOf(compilation));

        foreach (SyntaxTree tree in compilation.SyntaxTrees)
        {
            // Per file, because a semantic model and a walk of every node are what generation
            // costs, and a file is the unit both are paid in. A stop that waited for every
            // project's mutants would spend the margin a deadline leaves before the CI's own kill
            // (DEC0013).
            cancellationToken.ThrowIfCancellationRequested();

            if (SourceFile.IsGenerated(tree) || IsExcluded(tree) || !IsSelected(tree, selection))
            {
                continue;
            }

            // Mutators consult the model to reject replacements that would not compile, so it is
            // built once per tree rather than per node.
            SemanticModel semanticModel = compilation.GetSemanticModel(tree);

            foreach (SyntaxNode node in tree.GetRoot(cancellationToken).DescendantNodes())
            {
                // Three different refusals, for three different reasons: a mutation nothing could
                // notice - a constant, or a capacity (RB-033) - a mutation that could not compile,
                // and code the developer has said is not measured. The constant has one exception,
                // settled per rule below: one its own project reads, offered to a rule that reaches
                // constants (DEC0019).
                bool observable = MutationSite.IsObservable(node);

                if ((!observable && !_reachesConstants) ||
                    MutationSite.IsACapacity(node, semanticModel) ||
                    MutationSite.DeclaresAVariable(node) ||
                    MutationSite.AssignsOnSomePathsOnly(node) ||
                    MutationSite.IsExcludedFromCoverage(node, semanticModel))
                {
                    continue;
                }

                // Asked only once a rule has proposed something, and once per node: it binds every
                // identifier of the project that spells the constant's name, and most nodes of a
                // constant propose nothing.
                bool? readConstant = null;

                // Likewise once per node, and only once something is proposed: a call the project
                // does not hear still has its mutants generated, marked with the rule, so that the
                // report counts what was silenced rather than losing it (DEC0026).
                IReadOnlyList<string>? ignoredBy = null;

                foreach (IMutator mutator in _catalog.Mutators)
                {
                    if (!observable && !mutator.ReachesConstants)
                    {
                        continue;
                    }

                    foreach (MutationCandidate candidate in mutator.Mutate(node, semanticModel))
                    {
                        if (!observable &&
                            !(readConstant ??= MutationSite.IsAConstantItsProjectReads(
                                node, semanticModel, identifiers.Value)))
                        {
                            break;
                        }

                        ignoredBy ??= _ignoredCalls.RulesSilencing(node, semanticModel);

                        mutants.Add(new Mutant(_nextId, candidate, _root, ignoredBy));
                        _nextId = _nextId.Next();
                    }
                }
            }
        }

        return mutants;
    }

    /// <summary>
    /// True for a file the user asked to be left alone. It is still compiled - dropping it would
    /// change the assembly - but nothing in it is mutated.
    /// </summary>
    private bool IsExcluded(SyntaxTree tree) =>
        !string.IsNullOrEmpty(tree.FilePath) && _exclusions.Excludes(tree.FilePath);

    /// <summary>
    /// True when a partial run is judging this file, and always true for a full one.
    /// </summary>
    /// <remarks>
    /// A tree with no path is one nothing can have changed - it came from memory, not from the
    /// repository - so a partial run leaves it alone rather than guessing.
    /// </remarks>
    private static bool IsSelected(SyntaxTree tree, MutantSelection selection) =>
        selection.IsEverything ||
        (!string.IsNullOrEmpty(tree.FilePath) && selection.Includes(tree.FilePath));
}
