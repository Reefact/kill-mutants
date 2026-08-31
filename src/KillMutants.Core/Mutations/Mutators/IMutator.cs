using Microsoft.CodeAnalysis;

namespace KillMutants.Mutations.Mutators;

/// <summary>
/// A single mutation rule. Given a syntax node, proposes the changes it knows how to make.
/// </summary>
/// <remarks>
/// <para>
/// Implementations must return a <em>replacement node of the correct syntax kind</em>, never the
/// original node with a swapped operator token. This is the contract every mutator inherits, and it
/// is not cosmetic: Roslyn binds and emits from the node kind, so a token-level rewrite produces a
/// tree that prints as the mutation while emitting the original IL. Such a mutant is silently
/// equivalent to the original and is therefore always reported as survived - an invented gap in the
/// user's test suite, which baseline verification cannot detect because it guards against false
/// kills. See <c>docs/robustness-backlog-en.md</c>, entry RB-001.
/// </para>
/// <para>
/// Every mutator must be covered by a test asserting the resulting node's kind, and the catalogue as
/// a whole by a test asserting that a mutant's emitted assembly actually differs from the baseline.
/// </para>
/// </remarks>
internal interface IMutator
{
    /// <summary>Names this rule in reports.</summary>
    MutatorName Name { get; }

    /// <summary>Proposes the mutations that apply to <paramref name="node"/>, if any.</summary>
    /// <param name="node">The node to consider.</param>
    /// <param name="semanticModel">
    /// The semantic model for the node's tree. Implementations use it to reject a replacement that
    /// would not compile - a mutation that cannot build teaches nobody anything and costs a run.
    /// </param>
    IEnumerable<MutationCandidate> Mutate(SyntaxNode node, SemanticModel semanticModel);
}
