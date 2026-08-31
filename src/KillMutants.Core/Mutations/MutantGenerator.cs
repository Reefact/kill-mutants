using KillMutants.Mutations.Mutators;
using Microsoft.CodeAnalysis;

namespace KillMutants.Mutations;

/// <summary>Walks syntax trees and produces the mutants a catalog proposes for them.</summary>
internal sealed class MutantGenerator
{
    private readonly MutatorCatalog _catalog;

    private MutantId _nextId = MutantId.First;

    public MutantGenerator(MutatorCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        _catalog = catalog;
    }

    /// <summary>
    /// Produces every mutant the catalog proposes across <paramref name="syntaxTrees"/>, skipping
    /// generated sources and sites whose mutation could never be observed.
    /// </summary>
    /// <remarks>
    /// Identifiers continue across calls, so one generator numbers a whole run. A generator per
    /// project would otherwise restart at <c>M1</c> for each of them and make the report ambiguous.
    /// </remarks>
    public IReadOnlyList<Mutant> Generate(IEnumerable<SyntaxTree> syntaxTrees)
    {
        ArgumentNullException.ThrowIfNull(syntaxTrees);

        List<Mutant> mutants = [];

        foreach (SyntaxTree tree in syntaxTrees)
        {
            if (IsGenerated(tree))
            {
                continue;
            }

            foreach (SyntaxNode node in tree.GetRoot().DescendantNodes())
            {
                if (!MutationSite.IsObservable(node))
                {
                    continue;
                }

                foreach (IMutator mutator in _catalog.Mutators)
                {
                    foreach (MutationCandidate candidate in mutator.Mutate(node))
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
