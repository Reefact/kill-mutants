using KillMutants.Mutations;
using Microsoft.CodeAnalysis;

namespace KillMutants.Coverage;

/// <summary>
/// The distinct places in the source that mutants occupy, and which mutant stands for each.
/// </summary>
/// <remarks>
/// Several mutants share one expression: <c>age &gt;= 18</c> yields both a boundary shift and a
/// negation from the same node. Coverage is a property of the <em>site</em>, not of the mutant - a
/// test that reaches the expression reaches all of them - so the instrumented build carries one
/// recorder per site, and every mutant there inherits its answer. Instrumenting per mutant would
/// nest recorders inside each other and measure the same thing several times.
/// </remarks>
internal sealed class MutationSites
{
    private MutationSites(
        IReadOnlyDictionary<SyntaxNode, int> identifierByNode,
        IReadOnlyDictionary<MutantId, MutantId> representativeOf)
    {
        IdentifierByNode = identifierByNode;
        RepresentativeOf = representativeOf;
    }

    /// <summary>The recorder identifier to compile in at each distinct node.</summary>
    public IReadOnlyDictionary<SyntaxNode, int> IdentifierByNode { get; }

    /// <summary>For each mutant, the mutant whose identifier stands for its site.</summary>
    public IReadOnlyDictionary<MutantId, MutantId> RepresentativeOf { get; }

    /// <summary>Groups mutants by the expression they replace.</summary>
    public static MutationSites From(IEnumerable<Mutant> mutants)
    {
        ArgumentNullException.ThrowIfNull(mutants);

        Dictionary<SyntaxNode, int> identifierByNode = [];
        Dictionary<MutantId, MutantId> representativeOf = [];

        foreach (IGrouping<SyntaxNode, Mutant> atNode in mutants.GroupBy(mutant => mutant.OriginalNode))
        {
            MutantId representative = atNode.First().Id;

            identifierByNode[atNode.Key] = representative.Value;

            foreach (Mutant mutant in atNode)
            {
                representativeOf[mutant.Id] = representative;
            }
        }

        return new MutationSites(identifierByNode, representativeOf);
    }
}
