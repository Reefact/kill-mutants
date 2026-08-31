using Microsoft.CodeAnalysis;

namespace KillMutants.Mutations.Mutators;

/// <summary>
/// A single mutation rule. Given a syntax node, proposes the changes it knows how to make.
/// </summary>
/// <remarks>
/// Implementations must return a <em>replacement node of the correct syntax kind</em>, never the
/// original node with a swapped operator token. See <see cref="GreaterThanOrEqualMutator"/> for why
/// this distinction is not cosmetic.
/// </remarks>
internal interface IMutator
{
    /// <summary>Names this rule in reports.</summary>
    MutatorName Name { get; }

    /// <summary>Proposes the mutations that apply to <paramref name="node"/>, if any.</summary>
    IEnumerable<MutationCandidate> Mutate(SyntaxNode node);
}
