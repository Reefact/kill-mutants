using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KillMutants.Mutations.Mutators;

/// <summary>Turns <c>++</c> into <c>--</c> and back, in either position.</summary>
/// <remarks>
/// A loop counter that decrements instead of incrementing usually stops terminating, which is a
/// detection the timeout catches rather than an assertion; both count as the tests noticing.
/// </remarks>
internal sealed class IncrementMutator : IMutator
{
    /// <inheritdoc />
    public MutatorName Name { get; } = MutatorName.Create("Increment");

    /// <inheritdoc />
    public IEnumerable<MutationCandidate> Mutate(SyntaxNode node, SemanticModel semanticModel)
    {
        SyntaxKind mutated = node.Kind() switch
        {
            SyntaxKind.PostIncrementExpression => SyntaxKind.PostDecrementExpression,
            SyntaxKind.PostDecrementExpression => SyntaxKind.PostIncrementExpression,
            SyntaxKind.PreIncrementExpression => SyntaxKind.PreDecrementExpression,
            SyntaxKind.PreDecrementExpression => SyntaxKind.PreIncrementExpression,
            _ => SyntaxKind.None,
        };

        if (mutated == SyntaxKind.None)
        {
            yield break;
        }

        SyntaxKind token = mutated is SyntaxKind.PostIncrementExpression or SyntaxKind.PreIncrementExpression
            ? SyntaxKind.PlusPlusToken
            : SyntaxKind.MinusMinusToken;

        yield return new MutationCandidate(Name, node, Rewrite(node, mutated, token));
    }

    private static SyntaxNode Rewrite(SyntaxNode node, SyntaxKind mutated, SyntaxKind token) => node switch
    {
        PostfixUnaryExpressionSyntax postfix => SyntaxFactory
            .PostfixUnaryExpression(
                mutated,
                postfix.Operand,
                SyntaxFactory.Token(
                    postfix.OperatorToken.LeadingTrivia, token, postfix.OperatorToken.TrailingTrivia))
            .WithTriviaFrom(postfix),

        PrefixUnaryExpressionSyntax prefix => SyntaxFactory
            .PrefixUnaryExpression(
                mutated,
                SyntaxFactory.Token(
                    prefix.OperatorToken.LeadingTrivia, token, prefix.OperatorToken.TrailingTrivia),
                prefix.Operand)
            .WithTriviaFrom(prefix),

        _ => throw new ArgumentOutOfRangeException(nameof(node), node.Kind(), "Not an increment expression."),
    };
}
