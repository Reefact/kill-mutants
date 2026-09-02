using KillMutants.Filtering;
using KillMutants.Mutations.Mutators;
using KillMutants.Selection;
using Microsoft.CodeAnalysis;

namespace KillMutants.Mutations;

/// <summary>Walks syntax trees and produces the mutants a catalog proposes for them.</summary>
internal sealed class MutantGenerator
{
    private readonly MutatorCatalog _catalog;
    private readonly PathFilter _exclusions;
    private readonly string _root;

    private MutantId _nextId = MutantId.First;

    /// <param name="catalog">The rules to apply.</param>
    /// <param name="exclusions">Paths to leave alone.</param>
    /// <param name="root">
    /// The directory the run was pointed at. Mutants record their file relative to it, so that a
    /// mutant's key is the same on a CI runner and on a laptop - see <see cref="MutantKey"/>.
    /// </param>
    public MutantGenerator(MutatorCatalog catalog, PathFilter? exclusions = null, string? root = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        _catalog = catalog;
        _exclusions = exclusions ?? PathFilter.None;
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
    /// <remarks>
    /// Identifiers continue across calls, so one generator numbers a whole run. A generator per
    /// project would otherwise restart at <c>M1</c> for each of them and make the report ambiguous.
    /// A partial run numbers only what it generated, and its report says so: see
    /// <see cref="Reporting.RunScope"/>.
    /// </remarks>
    public IReadOnlyList<Mutant> Generate(Compilation compilation, MutantSelection? selection = null)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        selection ??= MutantSelection.Everything;

        List<Mutant> mutants = [];

        foreach (SyntaxTree tree in compilation.SyntaxTrees)
        {
            if (SourceFile.IsGenerated(tree) || IsExcluded(tree) || !IsSelected(tree, selection))
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
                        mutants.Add(new Mutant(_nextId, candidate, _root));
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
