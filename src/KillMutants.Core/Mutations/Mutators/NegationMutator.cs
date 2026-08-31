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
    public IEnumerable<MutationCandidate> Mutate(SyntaxNode node, SemanticModel semanticModel)
    {
        ArgumentNullException.ThrowIfNull(semanticModel);

        if (node is not PrefixUnaryExpressionSyntax unary ||
            !unary.IsKind(SyntaxKind.LogicalNotExpression) ||
            !KeepsItsType(unary, semanticModel))
        {
            yield break;
        }

        yield return new MutationCandidate(Name, unary, unary.Operand.WithTriviaFrom(unary));
    }

    /// <summary>
    /// True when dropping the negation leaves an expression of the same type.
    /// </summary>
    /// <remarks>
    /// A user-defined <c>operator !</c> may return something other than its operand's type, in which
    /// case removing it changes what the surrounding expression means, or stops it compiling.
    /// </remarks>
    private static bool KeepsItsType(PrefixUnaryExpressionSyntax unary, SemanticModel semanticModel)
    {
        ITypeSymbol? negated = semanticModel.GetTypeInfo(unary).Type;
        ITypeSymbol? operand = semanticModel.GetTypeInfo(unary.Operand).Type;

        return negated is not null && operand is not null &&
               SymbolEqualityComparer.Default.Equals(negated, operand);
    }
}
