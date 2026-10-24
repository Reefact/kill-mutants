using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KillMutants.Mutations.Mutators;

/// <summary>
/// Negates a decision that has no negation written, turning <c>if (ready)</c> into
/// <c>if (!ready)</c>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="NegationMutator"/> can only act where a <c>!</c> was already written, which on
/// Reefact/slugger was six sites (#18). This asks the same question of every other decision the
/// code makes - the condition of a branch or a loop, and a <c>bool</c> handed back to the caller -
/// and a decision no test can tell from its opposite is what a suite that never checks the branch
/// looks like.
/// </para>
/// <para>
/// A family of its own rather than part of <c>Negation</c>, because of what it costs. Measured on
/// slugger, it made 150 mutants where removal made 44, took a run limited to negations from fifteen
/// minutes to twenty-nine, and left 9 undetected; four of them were real gaps, in the equality of
/// two value types. Whether that trade is worth it is a project's call, and a family name is what
/// <c>--without</c> takes.
/// </para>
/// </remarks>
internal sealed class NegatedDecisionMutator : IMutator
{
    /// <inheritdoc />
    public MutatorName Name { get; } = MutatorName.Create("NegatedDecision");

    /// <inheritdoc />
    public IEnumerable<MutationCandidate> Mutate(SyntaxNode node, SemanticModel semanticModel)
    {
        ArgumentNullException.ThrowIfNull(semanticModel);

        if (node is ExpressionSyntax decision &&
            IsADecision(decision) &&
            AsksSomethingNoOtherFamilyAsks(decision) &&
            IsBoolean(decision, semanticModel))
        {
            yield return new MutationCandidate(Name, decision, Negated(decision));
        }
    }

    /// <summary>
    /// True when the expression is a decision the code acts on: the condition of a branch or a
    /// loop, or the value a member or a lambda hands back.
    /// </summary>
    /// <remarks>
    /// The condition of a ternary is deliberately not one of them. <c>c ? a : b</c> negated is
    /// <c>!c ? a : b</c>, which is <c>c ? b : a</c> - the branch swap the <c>Conditional</c> family
    /// already makes, so negating it would judge the same mutant twice under two names.
    /// </remarks>
    private static bool IsADecision(ExpressionSyntax expression) => expression.Parent switch
    {
        IfStatementSyntax branch => branch.Condition == expression,
        WhileStatementSyntax loop => loop.Condition == expression,
        DoStatementSyntax loop => loop.Condition == expression,
        ForStatementSyntax loop => loop.Condition == expression,
        ReturnStatementSyntax returned => returned.Expression == expression,
        ArrowExpressionClauseSyntax body => body.Expression == expression,
        LambdaExpressionSyntax lambda => lambda.Body == expression,
        _ => false,
    };

    /// <summary>
    /// True unless negating the expression would repeat a mutant another family already makes.
    /// </summary>
    /// <remarks>
    /// Three shapes are left to the family that owns them. A negation is undone by
    /// <c>Negation</c>, and negating it again would read <c>!!x</c>, the original under another
    /// spelling. A comparison is negated by <c>Comparison</c>: <c>!(a &gt; b)</c> is its
    /// <c>a &lt;= b</c>, and <c>!(a == b)</c> its <c>a != b</c>. And a literal is flipped by
    /// <c>BooleanLiteral</c>.
    /// </remarks>
    private static bool AsksSomethingNoOtherFamilyAsks(ExpressionSyntax expression)
    {
        ExpressionSyntax inner = expression;

        while (inner is ParenthesizedExpressionSyntax parenthesized)
        {
            inner = parenthesized.Expression;
        }

        return !inner.IsKind(SyntaxKind.LogicalNotExpression) &&
               !inner.IsKind(SyntaxKind.TrueLiteralExpression) &&
               !inner.IsKind(SyntaxKind.FalseLiteralExpression) &&
               !inner.IsKind(SyntaxKind.EqualsExpression) &&
               !inner.IsKind(SyntaxKind.NotEqualsExpression) &&
               !inner.IsKind(SyntaxKind.LessThanExpression) &&
               !inner.IsKind(SyntaxKind.LessThanOrEqualExpression) &&
               !inner.IsKind(SyntaxKind.GreaterThanExpression) &&
               !inner.IsKind(SyntaxKind.GreaterThanOrEqualExpression);
    }

    /// <summary>
    /// True when the expression is a <c>bool</c> and is used as one, so <c>!</c> is the built-in
    /// operator and the result fits where the expression stood.
    /// </summary>
    /// <remarks>
    /// A member returning <c>object</c> can hand back a <c>bool</c>, and a type of the user's own
    /// can convert to one; neither is a decision this rule can negate without changing what the
    /// surrounding code receives.
    /// </remarks>
    private static bool IsBoolean(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        TypeInfo type = semanticModel.GetTypeInfo(expression);

        return type.Type?.SpecialType == SpecialType.System_Boolean &&
               type.ConvertedType?.SpecialType == SpecialType.System_Boolean;
    }

    /// <summary>
    /// The decision with a <c>!</c> in front, as a new node of that kind - the RB-001 rule - and in
    /// parentheses unless what it negates already reads as one term.
    /// </summary>
    private static PrefixUnaryExpressionSyntax Negated(ExpressionSyntax decision)
    {
        ExpressionSyntax operand = ReadsAsOneTerm(decision)
            ? decision.WithoutTrivia()
            : SyntaxFactory.ParenthesizedExpression(decision.WithoutTrivia());

        return SyntaxFactory
            .PrefixUnaryExpression(SyntaxKind.LogicalNotExpression, operand)
            .WithTriviaFrom(decision);
    }

    private static bool ReadsAsOneTerm(ExpressionSyntax expression) =>
        expression is IdentifierNameSyntax
            or MemberAccessExpressionSyntax
            or InvocationExpressionSyntax
            or ElementAccessExpressionSyntax
            or ParenthesizedExpressionSyntax;
}
