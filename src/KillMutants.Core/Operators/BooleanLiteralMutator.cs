using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KillMutants.Operators;

/// <summary>Swaps a <c>true</c> literal for <c>false</c> and the other way round.</summary>
/// <remarks>
/// Only LITERALS, never an expression that happens to be boolean. Negating an arbitrary condition
/// is a different rule with different survivors, and folding the two together would make a report
/// unable to say which one a test failed to catch.
/// </remarks>
public sealed class BooleanLiteralMutator : IMutationOperator
{
    /// <inheritdoc />
    public IEnumerable<Mutation> Mutate(SyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (node is not LiteralExpressionSyntax literal)
        {
            yield break;
        }

        var replacement = literal.Kind() switch
        {
            SyntaxKind.TrueLiteralExpression  => "false",
            SyntaxKind.FalseLiteralExpression => "true",
            _                                 => null,
        };

        if (replacement is not null)
        {
            yield return new Mutation("boolean", literal.Span, literal.ToString(), replacement);
        }
    }
}
