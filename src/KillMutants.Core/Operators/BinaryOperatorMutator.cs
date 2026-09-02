using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KillMutants.Operators;

/// <summary>
/// Replaces a binary operator with another of the same shape: <c>-</c> for <c>+</c>,
/// <c>&lt;=</c> for <c>&lt;</c>, <c>!=</c> for <c>==</c>, and so on.
/// </summary>
/// <remarks>
/// <para>
/// One class covers arithmetic, relational, equality and logical operators because the rule is the
/// same in every case — swap the operator token — and only the table below differs. Four classes
/// would duplicate the walk and the span arithmetic to gain nothing but names, and each mutation
/// already reports which family it came from.
/// </para>
/// <para>
/// This is a SYNTAX-only rule, and one consequence is worth naming: <c>"a" + "b"</c> mutates to
/// <c>"a" - "b"</c>, which does not compile. Telling those apart needs the semantic model, which
/// needs a whole compilation rather than one file. Until the engine builds one, an uncompilable
/// mutant is a real outcome — it fails its build and counts as no result, never as a survivor.
/// </para>
/// </remarks>
public sealed class BinaryOperatorMutator : IMutationOperator
{
    private static readonly Dictionary<SyntaxKind, (string Family, SyntaxKind[] Replacements)> Table = new()
    {
        [SyntaxKind.PlusToken]               = ("arithmetic", [SyntaxKind.MinusToken]),
        [SyntaxKind.MinusToken]              = ("arithmetic", [SyntaxKind.PlusToken]),
        [SyntaxKind.AsteriskToken]           = ("arithmetic", [SyntaxKind.SlashToken]),
        [SyntaxKind.SlashToken]              = ("arithmetic", [SyntaxKind.AsteriskToken]),
        [SyntaxKind.PercentToken]            = ("arithmetic", [SyntaxKind.AsteriskToken]),

        [SyntaxKind.LessThanToken]           = ("relational", [SyntaxKind.LessThanEqualsToken, SyntaxKind.GreaterThanEqualsToken]),
        [SyntaxKind.LessThanEqualsToken]     = ("relational", [SyntaxKind.LessThanToken, SyntaxKind.GreaterThanToken]),
        [SyntaxKind.GreaterThanToken]        = ("relational", [SyntaxKind.GreaterThanEqualsToken, SyntaxKind.LessThanEqualsToken]),
        [SyntaxKind.GreaterThanEqualsToken]  = ("relational", [SyntaxKind.GreaterThanToken, SyntaxKind.LessThanToken]),

        [SyntaxKind.EqualsEqualsToken]       = ("equality",   [SyntaxKind.ExclamationEqualsToken]),
        [SyntaxKind.ExclamationEqualsToken]  = ("equality",   [SyntaxKind.EqualsEqualsToken]),

        [SyntaxKind.AmpersandAmpersandToken] = ("logical",    [SyntaxKind.BarBarToken]),
        [SyntaxKind.BarBarToken]             = ("logical",    [SyntaxKind.AmpersandAmpersandToken]),
    };

    /// <inheritdoc />
    public IEnumerable<Mutation> Mutate(SyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (node is not BinaryExpressionSyntax binary ||
            !Table.TryGetValue(binary.OperatorToken.Kind(), out var entry))
        {
            yield break;
        }

        var token = binary.OperatorToken;
        foreach (var replacement in entry.Replacements)
        {
            // The token's span excludes its surrounding trivia, so replacing it leaves the original
            // spacing untouched and a mutated file differs by these characters alone.
            yield return new Mutation(entry.Family, token.Span, token.Text, SyntaxFacts.GetText(replacement));
        }
    }
}
