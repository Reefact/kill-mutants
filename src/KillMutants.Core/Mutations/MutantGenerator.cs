using KillMutants.Mutations.Mutators;
using Microsoft.CodeAnalysis;

namespace KillMutants.Mutations;

/// <summary>Walks syntax trees and produces the mutants a catalog proposes for them.</summary>
internal sealed class MutantGenerator
{
    private readonly MutatorCatalog _catalog;

    public MutantGenerator(MutatorCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        _catalog = catalog;
    }

    /// <summary>
    /// Produces every mutant the catalog proposes across <paramref name="syntaxTrees"/>, skipping
    /// generated sources.
    /// </summary>
    public IReadOnlyList<Mutant> Generate(IEnumerable<SyntaxTree> syntaxTrees)
    {
        ArgumentNullException.ThrowIfNull(syntaxTrees);

        List<Mutant> mutants = [];
        MutantId nextId = MutantId.First;

        foreach (SyntaxTree tree in syntaxTrees)
        {
            if (IsGenerated(tree))
            {
                continue;
            }

            foreach (SyntaxNode node in tree.GetRoot().DescendantNodes())
            {
                foreach (IMutator mutator in _catalog.Mutators)
                {
                    foreach (MutationCandidate candidate in mutator.Mutate(node))
                    {
                        mutants.Add(new Mutant(nextId, candidate));
                        nextId = nextId.Next();
                    }
                }
            }
        }

        return mutants;
    }

    /// <summary>
    /// Generated sources are compiler inputs, not the developer's code. Mutating
    /// <c>AssemblyInfo.cs</c> or <c>GlobalUsings.g.cs</c> would report findings against code nobody
    /// wrote and cannot fix. They must still be <em>compiled</em> - dropping them changes the
    /// assembly identity - so they are excluded here rather than from the compilation.
    /// </summary>
    private static bool IsGenerated(SyntaxTree tree)
    {
        string path = tree.FilePath;

        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        if (path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string separator = Path.DirectorySeparatorChar.ToString();

        return path.Contains(separator + "obj" + separator, StringComparison.OrdinalIgnoreCase);
    }
}
