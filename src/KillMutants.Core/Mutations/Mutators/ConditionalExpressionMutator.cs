using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KillMutants.Mutations.Mutators;

/// <summary>
/// Forces the condition of a conditional expression each way while still evaluating it:
/// <c>c ? a : b</c> becomes <c>(c || true) ? a : b</c> and <c>(c &amp;&amp; false) ? a : b</c>.
/// </summary>
/// <remarks>
/// <para>
/// Each mutant asks about one branch. The condition forced to true survives when no test that takes
/// the other branch checks the result, and the one forced to false the same way round, so a survivor
/// names the branch nothing verifies. The branch swap this family made before, <c>c ? b : a</c>, dies
/// exactly when one of the two does, so it is not made beside them - DEC0017.
/// </para>
/// <para>
/// The forced value is written beside the condition rather than in its place, which is where this
/// departs from Stryker.NET's <c>true ? a : b</c>: <c>c</c> is still evaluated, so a call in it keeps
/// its side effects (RB-015), and neither form is a compile-time constant, so neither changes what the
/// compiler concludes about reachability (RB-029).
/// </para>
/// </remarks>
internal sealed class ConditionalExpressionMutator : IMutator
{
    /// <inheritdoc />
    public MutatorName Name { get; } = MutatorName.Create("Conditional");

    /// <inheritdoc />
    public IEnumerable<MutationCandidate> Mutate(SyntaxNode node, SemanticModel semanticModel)
    {
        ArgumentNullException.ThrowIfNull(semanticModel);

        // Forcing either way picks one of two identical branches, which behaves exactly like the
        // original: guaranteed to survive, and for a reason that says nothing about the tests.
        if (node is not ConditionalExpressionSyntax conditional ||
            SyntaxFactory.AreEquivalent(conditional.WhenTrue, conditional.WhenFalse) ||
            !IsADecisionTakenAtRunTime(conditional.Condition, semanticModel))
        {
            yield break;
        }

        foreach (ConditionalExpressionSyntax rewritten in new[]
                 {
                     Forced(conditional, SyntaxKind.LogicalOrExpression, SyntaxKind.TrueLiteralExpression),
                     Forced(conditional, SyntaxKind.LogicalAndExpression, SyntaxKind.FalseLiteralExpression),
                 })
        {
            if (Binds(rewritten, conditional, semanticModel))
            {
                yield return new MutationCandidate(Name, conditional, rewritten);
            }
        }
    }

    /// <summary>
    /// True when the condition is a <c>bool</c> the compiler does not already know.
    /// </summary>
    /// <remarks>
    /// A condition of a type with its own <c>operator true</c> is not a <c>bool</c>, and
    /// <c>c || true</c> may mean something else or nothing at all for it. A constant condition was
    /// settled when the code was compiled, so there is no decision to force.
    /// </remarks>
    private static bool IsADecisionTakenAtRunTime(ExpressionSyntax condition, SemanticModel semanticModel)
    {
        TypeInfo type = semanticModel.GetTypeInfo(condition);

        return type.Type?.SpecialType == SpecialType.System_Boolean &&
               type.ConvertedType?.SpecialType == SpecialType.System_Boolean &&
               !semanticModel.GetConstantValue(condition).HasValue;
    }

    /// <summary>
    /// The conditional with its condition combined with a literal, as a new node of that kind - the
    /// RB-001 rule - and the combination in parentheses so that it reads as the condition.
    /// </summary>
    private static ConditionalExpressionSyntax Forced(
        ConditionalExpressionSyntax conditional, SyntaxKind combination, SyntaxKind literal)
    {
        ExpressionSyntax condition = conditional.Condition.WithoutTrivia();
        ExpressionSyntax operand = ReadsAsOneTerm(condition)
            ? condition
            : SyntaxFactory.ParenthesizedExpression(condition);

        SyntaxToken @operator = SyntaxFactory.Token(
            SyntaxFactory.TriviaList(SyntaxFactory.Space),
            combination == SyntaxKind.LogicalOrExpression ? SyntaxKind.BarBarToken : SyntaxKind.AmpersandAmpersandToken,
            SyntaxFactory.TriviaList(SyntaxFactory.Space));

        ParenthesizedExpressionSyntax forced = SyntaxFactory.ParenthesizedExpression(
            SyntaxFactory.BinaryExpression(combination, operand, @operator, SyntaxFactory.LiteralExpression(literal)));

        return SyntaxFactory
            .ConditionalExpression(
                forced.WithTriviaFrom(conditional.Condition),
                conditional.QuestionToken,
                conditional.WhenTrue,
                conditional.ColonToken,
                conditional.WhenFalse)
            .WithTriviaFrom(conditional);
    }

    private static bool ReadsAsOneTerm(ExpressionSyntax expression) =>
        expression is IdentifierNameSyntax
            or MemberAccessExpressionSyntax
            or InvocationExpressionSyntax
            or ElementAccessExpressionSyntax
            or ParenthesizedExpressionSyntax
            or PrefixUnaryExpressionSyntax;

    /// <summary>True when the forced conditional still means something the compiler accepts.</summary>
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
