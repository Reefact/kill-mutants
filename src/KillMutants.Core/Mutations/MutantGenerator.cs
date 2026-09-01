using KillMutants.Filtering;
using KillMutants.Mutations.Mutators;
using Microsoft.CodeAnalysis;

namespace KillMutants.Mutations;

/// <summary>Walks syntax trees and produces the mutants a catalog proposes for them.</summary>
internal sealed class MutantGenerator
{
    private readonly MutatorCatalog _catalog;
    private readonly PathFilter _exclusions;

    private MutantId _nextId = MutantId.First;

    public MutantGenerator(MutatorCatalog catalog, PathFilter? exclusions = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        _catalog = catalog;
        _exclusions = exclusions ?? PathFilter.None;
    }

    /// <summary>
    /// Produces every mutant the catalog proposes across <paramref name="compilation"/>, skipping
    /// generated sources - see <see cref="SourceFile"/> - and sites whose mutation could never be
    /// observed. Generated files are still <em>compiled</em>: dropping them would change the
    /// assembly's identity.
    /// </summary>
    /// <remarks>
    /// Identifiers continue across calls, so one generator numbers a whole run. A generator per
    /// project would otherwise restart at <c>M1</c> for each of them and make the report ambiguous.
    /// </remarks>
    public IReadOnlyList<Mutant> Generate(Compilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        List<Mutant> mutants = [];

        foreach (SyntaxTree tree in compilation.SyntaxTrees)
        {
            if (SourceFile.IsGenerated(tree) || IsExcluded(tree))
            {
                continue;
            }

            // Mutators consult the model to reject replacements that would not compile, so it is
            // built once per tree rather than per node.
            SemanticModel semanticModel = compilation.GetSemanticModel(tree);

            foreach (SyntaxNode node in tree.GetRoot().DescendantNodes())
            {
                // Three different refusals, for three different reasons: a mutation nothing could
                // notice, a mutation that could not compile, and code the developer has said is not
                // measured.
                if (!MutationSite.IsObservable(node) ||
                    MutationSite.DeclaresAVariable(node) ||
                    MutationSite.IsExcludedFromCoverage(node, semanticModel))
                {
                    continue;
                }

                foreach (IMutator mutator in _catalog.Mutators)
                {
                    foreach (MutationCandidate candidate in mutator.Mutate(node, semanticModel))
                    {
                        mutants.Add(new Mutant(_nextId, candidate));
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
}
