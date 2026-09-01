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
    public IEnumerable<MutationCandidate> Mutate(SyntaxNode node, SemanticModel semanticModel)
    {
        ArgumentNullException.ThrowIfNull(semanticModel);

        if (node is not BinaryExpressionSyntax binary ||
            !Replacements.TryGetValue(binary.Kind(), out IReadOnlyList<SyntaxKind>? replacements))
        {
            yield break;
        }

        foreach (SyntaxKind replacement in replacements)
        {
            BinaryExpressionSyntax rewritten = Rewrite(binary, replacement);

            if (Binds(rewritten, binary, semanticModel))
            {
                yield return new MutationCandidate(Name, binary, rewritten);
            }
        }
    }

    /// <summary>
    /// True when the replacement operator actually exists for these operands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asking the compiler is both simpler and more complete than maintaining a list of forbidden
    /// cases. In one rule it rules out string concatenation (there is no <c>string - string</c>),
    /// user-defined types declaring only one operator of a pair, and every case nobody has thought
    /// of yet — while allowing delegates, where both <c>+</c> and <c>-</c> are defined.
    /// </para>
    /// <para>
    /// The test is on the resulting <em>type</em>, not on the symbol, and that distinction is load
    /// bearing. Verified against Roslyn 5.9: <c>a &amp;&amp; b</c> rewritten to <c>a || b</c> binds
    /// to a null symbol — the conditional operators on <c>bool</c> have no operator method — while
    /// still yielding type <c>bool</c>. A symbol-based check silently discards every logical mutant.
    /// A replacement that genuinely cannot compile yields the error type instead.
    /// </para>
    /// <para>
    /// A mutant that cannot compile is a correct outcome, but a useless one: it costs analysis,
    /// clutters the report and teaches nobody anything. Better not to propose it.
    /// </para>
    /// </remarks>
    private static bool Binds(
        BinaryExpressionSyntax replacement,
        BinaryExpressionSyntax original,
        SemanticModel semanticModel)
    {
        ITypeSymbol? type = semanticModel.GetSpeculativeTypeInfo(
            original.SpanStart, replacement, SpeculativeBindingOption.BindAsExpression).Type;

        return type is not null && type.TypeKind != TypeKind.Error;
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
        SyntaxKind.AddExpression => SyntaxKind.PlusToken,
        SyntaxKind.SubtractExpression => SyntaxKind.MinusToken,
        SyntaxKind.MultiplyExpression => SyntaxKind.AsteriskToken,
        SyntaxKind.DivideExpression => SyntaxKind.SlashToken,
        SyntaxKind.ModuloExpression => SyntaxKind.PercentToken,
        SyntaxKind.BitwiseAndExpression => SyntaxKind.AmpersandToken,
        SyntaxKind.BitwiseOrExpression => SyntaxKind.BarToken,
        SyntaxKind.ExclusiveOrExpression => SyntaxKind.CaretToken,
        SyntaxKind.LeftShiftExpression => SyntaxKind.LessThanLessThanToken,
        SyntaxKind.RightShiftExpression => SyntaxKind.GreaterThanGreaterThanToken,
        _ => throw new ArgumentOutOfRangeException(
            nameof(expressionKind), expressionKind, "No operator token is known for this expression kind."),
    };
}
