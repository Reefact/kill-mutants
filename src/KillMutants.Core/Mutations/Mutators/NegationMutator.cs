using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KillMutants.Mutations.Mutators;

/// <summary>Removes a logical negation, turning <c>!condition</c> into <c>condition</c>.</summary>
/// <remarks>
/// Deleting the operator rather than adding one keeps the mutant readable and avoids the double
/// negations that an insert-based rule produces.
/// </remarks>
internal sealed class NegationMutator : IMutator
{
    /// <inheritdoc />
    public MutatorName Name { get; } = MutatorName.Create("Negation");

    /// <inheritdoc />
    public IEnumerable<MutationCandidate> Mutate(SyntaxNode node)
    {
        if (node is not PrefixUnaryExpressionSyntax unary ||
            !unary.IsKind(SyntaxKind.LogicalNotExpression))
        {
            yield break;
        }

        yield return new MutationCandidate(Name, unary, unary.Operand.WithTriviaFrom(unary));
    }
}
