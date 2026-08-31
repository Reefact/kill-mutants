using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KillMutants.Mutations.Mutators;

/// <summary>
/// Shared machinery for mutators that swap one binary operator for another.
/// </summary>
/// <remarks>
/// This base exists because three families genuinely repeat the same work, not in anticipation of a
/// fourth. It also enforces the rule that matters most: a replacement is built as a new node of the
/// target <em>kind</em>. Roslyn binds and emits from the node kind, so a mutator that swapped only
/// the operator token would emit the original IL and its mutants would always be reported survived.
/// Concentrating that here means no future family can get it wrong by accident.
/// </remarks>
internal abstract class BinaryOperatorMutator : IMutator
{
    /// <inheritdoc />
    public abstract MutatorName Name { get; }

    /// <summary>The operators this family rewrites, and what it rewrites each one into.</summary>
    protected abstract IReadOnlyDictionary<SyntaxKind, IReadOnlyList<SyntaxKind>> Replacements { get; }

    /// <inheritdoc />
    public IEnumerable<MutationCandidate> Mutate(SyntaxNode node)
    {
        if (node is not BinaryExpressionSyntax binary ||
            !Replacements.TryGetValue(binary.Kind(), out IReadOnlyList<SyntaxKind>? replacements))
        {
            yield break;
        }

        foreach (SyntaxKind replacement in replacements)
        {
            yield return new MutationCandidate(Name, binary, Rewrite(binary, replacement));
        }
    }

    private static BinaryExpressionSyntax Rewrite(BinaryExpressionSyntax original, SyntaxKind expressionKind)
    {
        SyntaxToken operatorToken = SyntaxFactory.Token(
            original.OperatorToken.LeadingTrivia,
            OperatorTokenFor(expressionKind),
            original.OperatorToken.TrailingTrivia);

        return SyntaxFactory
            .BinaryExpression(expressionKind, original.Left, operatorToken, original.Right)
            .WithTriviaFrom(original);
    }

    private static SyntaxKind OperatorTokenFor(SyntaxKind expressionKind) => expressionKind switch
    {
        SyntaxKind.LessThanExpression => SyntaxKind.LessThanToken,
        SyntaxKind.LessThanOrEqualExpression => SyntaxKind.LessThanEqualsToken,
        SyntaxKind.GreaterThanExpression => SyntaxKind.GreaterThanToken,
        SyntaxKind.GreaterThanOrEqualExpression => SyntaxKind.GreaterThanEqualsToken,
        SyntaxKind.EqualsExpression => SyntaxKind.EqualsEqualsToken,
        SyntaxKind.NotEqualsExpression => SyntaxKind.ExclamationEqualsToken,
        SyntaxKind.LogicalAndExpression => SyntaxKind.AmpersandAmpersandToken,
        SyntaxKind.LogicalOrExpression => SyntaxKind.BarBarToken,
        _ => throw new ArgumentOutOfRangeException(
            nameof(expressionKind), expressionKind, "No operator token is known for this expression kind."),
    };
}
