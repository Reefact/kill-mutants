using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KillMutants.Mutations.Mutators;

/// <summary>Swaps <c>true</c> and <c>false</c>.</summary>
/// <remarks>
/// Blunt, and effective for exactly that reason: a literal nobody's test depends on is a literal
/// nobody's test is checking.
/// </remarks>
internal sealed class BooleanLiteralMutator : IMutator
{
    /// <inheritdoc />
    public MutatorName Name { get; } = MutatorName.Create("BooleanLiteral");

    /// <inheritdoc />
    public IEnumerable<MutationCandidate> Mutate(SyntaxNode node, SemanticModel semanticModel)
    {
        if (node is not LiteralExpressionSyntax literal)
        {
            yield break;
        }

        SyntaxKind replacement = literal.Kind() switch
        {
            SyntaxKind.TrueLiteralExpression => SyntaxKind.FalseLiteralExpression,
            SyntaxKind.FalseLiteralExpression => SyntaxKind.TrueLiteralExpression,
            _ => SyntaxKind.None,
        };

        if (replacement == SyntaxKind.None)
        {
            yield break;
        }

        SyntaxToken token = SyntaxFactory.Token(
            literal.Token.LeadingTrivia,
            replacement == SyntaxKind.TrueLiteralExpression ? SyntaxKind.TrueKeyword : SyntaxKind.FalseKeyword,
            literal.Token.TrailingTrivia);

        yield return new MutationCandidate(
            Name,
            literal,
            SyntaxFactory.LiteralExpression(replacement, token).WithTriviaFrom(literal));
    }
}
