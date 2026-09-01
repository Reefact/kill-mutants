using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KillMutants.Mutations.Mutators;

/// <summary>Swaps the branches of a conditional expression: <c>c ? a : b</c> becomes <c>c ? b : a</c>.</summary>
/// <remarks>
/// Swapping the branches rather than negating the condition keeps the mutation on the ternary itself,
/// so it still applies when the condition is a bare identifier or call that no other family touches -
/// <c>IsAdult(age) ? "adult" : "minor"</c> has no operator to mutate.
/// </remarks>
internal sealed class ConditionalExpressionMutator : IMutator
{
    /// <inheritdoc />
    public MutatorName Name { get; } = MutatorName.Create("Conditional");

    /// <inheritdoc />
    public IEnumerable<MutationCandidate> Mutate(SyntaxNode node, SemanticModel semanticModel)
    {
        ArgumentNullException.ThrowIfNull(semanticModel);

        // Swapping two identical branches produces a mutant that behaves exactly like the original:
        // guaranteed to survive, and for a reason that says nothing about the tests.
        if (node is not ConditionalExpressionSyntax conditional ||
            SyntaxFactory.AreEquivalent(conditional.WhenTrue, conditional.WhenFalse))
        {
            yield break;
        }

        ConditionalExpressionSyntax rewritten = SyntaxFactory
            .ConditionalExpression(
                conditional.Condition,
                conditional.QuestionToken,
                conditional.WhenFalse.WithTriviaFrom(conditional.WhenTrue),
                conditional.ColonToken,
                conditional.WhenTrue.WithTriviaFrom(conditional.WhenFalse))
            .WithTriviaFrom(conditional);

        if (Binds(rewritten, conditional, semanticModel))
        {
            yield return new MutationCandidate(Name, conditional, rewritten);
        }
    }

    /// <summary>True when the swapped conditional still means something the compiler accepts.</summary>
    /// <remarks>
    /// A conditional need not have a natural type - <c>flag ? 1 : null</c> only has one once a target
    /// type is known - so a null type is not evidence of a problem here and must not be rejected the
    /// way <see cref="BinaryOperatorMutator"/> rejects it. Only an explicit error type is.
    /// </remarks>
    private static bool Binds(
        ConditionalExpressionSyntax replacement,
        ConditionalExpressionSyntax original,
        SemanticModel semanticModel)
    {
        ITypeSymbol? type = semanticModel.GetSpeculativeTypeInfo(
            original.SpanStart, replacement, SpeculativeBindingOption.BindAsExpression).Type;

        return type is not { TypeKind: TypeKind.Error };
    }
}
