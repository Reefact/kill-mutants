using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KillMutants.Mutations.Mutators;

/// <summary>
/// Empties a string literal, or fills an empty one.
/// </summary>
/// <remarks>
/// A surviving mutant here says the text was never asserted on — a common gap in code that builds
/// messages, keys or paths. Only ordinary literals are touched: interpolated and verbatim-with-content
/// forms are left for a later milestone, and literals in places the compiler folds into callers are
/// already excluded by <see cref="MutationSite"/>.
/// </remarks>
internal sealed class StringLiteralMutator : IMutator
{
    private const string NonEmptyReplacement = "KillMutants";

    /// <inheritdoc />
    public MutatorName Name { get; } = MutatorName.Create("StringLiteral");

    /// <inheritdoc />
    public IEnumerable<MutationCandidate> Mutate(SyntaxNode node, SemanticModel semanticModel)
    {
        if (node is not LiteralExpressionSyntax literal ||
            !literal.IsKind(SyntaxKind.StringLiteralExpression) ||
            literal.Token.ValueText is not { } text)
        {
            yield break;
        }

        string replacement = text.Length == 0 ? NonEmptyReplacement : string.Empty;

        yield return new MutationCandidate(
            Name,
            literal,
            SyntaxFactory
                .LiteralExpression(
                    SyntaxKind.StringLiteralExpression,
                    SyntaxFactory.Literal(replacement))
                .WithTriviaFrom(literal));
    }
}
