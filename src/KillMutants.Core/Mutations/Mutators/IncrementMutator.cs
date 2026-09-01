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
        ArgumentNullException.ThrowIfNull(semanticModel);

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

        SyntaxNode rewritten = Rewrite(node, mutated, token);

        if (Binds(rewritten, node, semanticModel))
        {
            yield return new MutationCandidate(Name, node, rewritten);
        }
    }

    /// <summary>True when the opposite operator actually exists for this operand.</summary>
    /// <remarks>
    /// <para>
    /// The same rule the binary families use, and for the same reason: <c>++</c> and <c>--</c> are a
    /// pair only for the built-in numeric types. A user-defined type may declare one and not the
    /// other, and then the mutant does not compile - measured as
    /// <c>CS0023: Operator '--' cannot be applied to operand of type 'Counter'</c>.
    /// </para>
    /// <para>
    /// Verified against the .NET 10 SDK for both forms this applies to: the classic
    /// <c>static Counter operator ++(Counter)</c>, and C# 14's user-defined <em>instance</em>
    /// operator <c>public void operator ++()</c>. Asking the compiler covers whatever C# adds next
    /// without this family having to know about it.
    /// </para>
    /// </remarks>
    private static bool Binds(SyntaxNode replacement, SyntaxNode original, SemanticModel semanticModel)
    {
        ITypeSymbol? type = semanticModel.GetSpeculativeTypeInfo(
            original.SpanStart, replacement, SpeculativeBindingOption.BindAsExpression).Type;

        return type is not null && type.TypeKind != TypeKind.Error;
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
