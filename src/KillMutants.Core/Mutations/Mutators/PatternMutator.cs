using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace KillMutants.Mutations.Mutators;

/// <summary>
/// Mutates the patterns of an <c>is</c> expression - and the type test <c>x is T</c>, which C# parses
/// as an operator but reads as a pattern: the whole pattern negated, a relational pattern's
/// boundary shifted or negated, and each arm of an <c>or</c> or <c>and</c> dropped.
/// </summary>
/// <remarks>
/// <para>
/// Code written in modern C# decides in patterns as often as with operators, and no operator family
/// sees them: <c>Comparison</c> asks about <c>x &gt; 3</c> and nothing asked about <c>x is &gt; 3</c>
/// (#19). The three mutations here are the pattern twins of questions the catalogue already asks -
/// DEC0018.
/// </para>
/// <para>
/// Only the patterns of an <c>is</c>. A pattern in a <c>switch</c> can be mutated into one that makes
/// a later arm unreachable, which the compiler refuses for the whole switch (CS8510), and that is not
/// something asking about one node can see.
/// </para>
/// </remarks>
internal sealed class PatternMutator : IMutator
{
    /// <summary>The boundary shift, then the negation - the two <c>Comparison</c> makes.</summary>
    private static readonly Dictionary<SyntaxKind, SyntaxKind[]> RelationalReplacements = new()
    {
        [SyntaxKind.LessThanToken] = [SyntaxKind.LessThanEqualsToken, SyntaxKind.GreaterThanEqualsToken],
        [SyntaxKind.LessThanEqualsToken] = [SyntaxKind.LessThanToken, SyntaxKind.GreaterThanToken],
        [SyntaxKind.GreaterThanToken] = [SyntaxKind.GreaterThanEqualsToken, SyntaxKind.LessThanEqualsToken],
        [SyntaxKind.GreaterThanEqualsToken] = [SyntaxKind.GreaterThanToken, SyntaxKind.LessThanToken],
    };

    /// <inheritdoc />
    public MutatorName Name { get; } = MutatorName.Create("Pattern");

    /// <inheritdoc />
    public IEnumerable<MutationCandidate> Mutate(SyntaxNode node, SemanticModel semanticModel)
    {
        ArgumentNullException.ThrowIfNull(semanticModel);

        IEnumerable<SyntaxNode> replacements = node switch
        {
            IsPatternExpressionSyntax test when IsDecidedAtRunTime(test, semanticModel) => [Negated(test)],
            BinaryExpressionSyntax test when test.IsKind(SyntaxKind.IsExpression) &&
                                             test.Right is TypeSyntax type &&
                                             IsDecidedAtRunTime(test, semanticModel) =>
                [IsInsideAnExpressionTree(test, semanticModel) ? NegatedAsAnOperator(test) : NegatedTypeTest(test, type, semanticModel)],
            RelationalPatternSyntax relational when BelongsToAnIsExpression(relational) => Shifted(relational),
            BinaryPatternSyntax binary when BelongsToAnIsExpression(binary) => Arms(binary, semanticModel),
            _ => [],
        };

        foreach (SyntaxNode replacement in replacements)
        {
            yield return new MutationCandidate(Name, node, replacement.WithTriviaFrom(node));
        }
    }

    /// <summary>
    /// <c>x is P</c> as <c>x is not P</c>, and <c>x is not P</c> as <c>x is P</c>.
    /// </summary>
    /// <remarks>
    /// Written as a pattern rather than as <c>!(x is P)</c>, which <c>NegatedDecision</c> leaves to
    /// this family. An <c>or</c> or an <c>and</c> is put in parentheses first, since <c>not</c> binds
    /// tighter than either and <c>x is not A or B</c> means <c>(not A) or B</c>.
    /// </remarks>
    private static IsPatternExpressionSyntax Negated(IsPatternExpressionSyntax test)
    {
        PatternSyntax pattern = test.Pattern.WithoutTrivia();

        PatternSyntax negated = pattern is UnaryPatternSyntax not && not.IsKind(SyntaxKind.NotPattern)
            ? not.Pattern.WithoutTrivia()
            : SyntaxFactory.UnaryPattern(
                SyntaxFactory.Token(SyntaxKind.NotKeyword).WithTrailingTrivia(SyntaxFactory.Space),
                pattern is BinaryPatternSyntax ? SyntaxFactory.ParenthesizedPattern(pattern) : pattern);

        return test.WithPattern(negated.WithTriviaFrom(test.Pattern));
    }

    /// <summary><c>x is T</c> as <c>!(x is T)</c>, for where a pattern cannot stand.</summary>
    /// <remarks>
    /// An expression tree accepts the type-test operator and no pattern at all (CS8122), so a type
    /// test inside one keeps its operator and is negated around it, as <c>NegatedDecision</c> did
    /// before it left the <c>is</c> to this family. Found in review of #38.
    /// </remarks>
    private static PrefixUnaryExpressionSyntax NegatedAsAnOperator(BinaryExpressionSyntax test) =>
        SyntaxFactory.PrefixUnaryExpression(
            SyntaxKind.LogicalNotExpression,
            SyntaxFactory.ParenthesizedExpression(test.WithoutTrivia()));

    /// <summary>True when the expression is part of a lambda the compiler turns into an expression tree.</summary>
    private static bool IsInsideAnExpressionTree(SyntaxNode node, SemanticModel semanticModel) =>
        node.Ancestors().OfType<AnonymousFunctionExpressionSyntax>().Any(lambda =>
            semanticModel.GetTypeInfo(lambda).ConvertedType is INamedTypeSymbol type &&
            IsAnExpressionTreeType(type));

    private static bool IsAnExpressionTreeType(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString() == "System.Linq.Expressions.LambdaExpression") { return true; }
        }

        return false;
    }

    /// <summary><c>x is T</c>, the type test that predates patterns, as <c>x is not T</c>.</summary>
    /// <remarks>
    /// <c>x is MimicStyle.Force</c> reads as a type test too, and the compiler only finds out it names
    /// a constant when it binds it - so the semantic model decides which pattern is written. Measured
    /// on Reefact/slugger: written as a type pattern, an enum member fails with CS0426.
    /// </remarks>
    private static IsPatternExpressionSyntax NegatedTypeTest(
        BinaryExpressionSyntax test, TypeSyntax type, SemanticModel semanticModel)
    {
        TypeSyntax tested = type.WithoutTrivia();
        PatternSyntax pattern = semanticModel.GetSymbolInfo(type).Symbol is ITypeSymbol
            ? SyntaxFactory.TypePattern(tested)
            : SyntaxFactory.ConstantPattern(tested);

        return SyntaxFactory.IsPatternExpression(
            test.Left,
            test.OperatorToken,
            SyntaxFactory.UnaryPattern(
                    SyntaxFactory.Token(SyntaxKind.NotKeyword).WithTrailingTrivia(SyntaxFactory.Space),
                    pattern)
                .WithTriviaFrom(type));
    }

    /// <summary>
    /// The boundary shift and the negation of a relational pattern, as <c>Comparison</c> makes them.
    /// </summary>
    /// <remarks>
    /// When the relational pattern is the whole of the <c>is</c>, its negation is left out: for
    /// <c>x is &gt; 1</c>, <c>x is &lt;= 1</c> asks what <c>x is not &gt; 1</c> already asks, and the
    /// two differ only on a <c>null</c> no test of a number is about.
    /// </remarks>
    private static IEnumerable<RelationalPatternSyntax> Shifted(RelationalPatternSyntax relational)
    {
        if (!RelationalReplacements.TryGetValue(relational.OperatorToken.Kind(), out SyntaxKind[]? kinds))
        {
            yield break;
        }

        bool isTheWholePattern = relational.Parent is IsPatternExpressionSyntax;

        foreach (SyntaxKind kind in isTheWholePattern ? kinds[..1] : kinds)
        {
            yield return relational.WithOperatorToken(
                SyntaxFactory.Token(kind).WithTriviaFrom(relational.OperatorToken));
        }
    }

    /// <summary>Each arm of an <c>or</c> or an <c>and</c>, standing alone in its place.</summary>
    /// <remarks>
    /// <para>
    /// Dropping an arm asks whether a test needs it: <c>s is "." or ".."</c> as <c>s is "."</c>
    /// survives when nothing checks <c>".."</c>. Swapping <c>or</c> for <c>and</c>, as Stryker.NET
    /// does, mostly makes a pattern that never matches, which says nothing about which arm is tested.
    /// </para>
    /// <para>
    /// An <c>and</c> never loses a <c>not null</c> or an empty <c>{ }</c>: the arm beside it nearly
    /// always rejects <c>null</c> already, as <c>x is not null and { Length: &gt; 0 }</c> does, and
    /// the mutant would behave exactly like the original.
    /// </para>
    /// </remarks>
    private static IEnumerable<PatternSyntax> Arms(BinaryPatternSyntax binary, SemanticModel semanticModel)
    {
        foreach ((PatternSyntax kept, PatternSyntax dropped) in new[] { (binary.Left, binary.Right), (binary.Right, binary.Left) })
        {
            if (binary.IsKind(SyntaxKind.AndPattern) &&
                (OnlyRejectsNull(dropped) || ReliesOnTheTypeTheOtherArmNarrows(kept, binary, semanticModel)))
            {
                continue;
            }

            yield return kept.WithoutTrivia();
        }
    }

    /// <summary>
    /// True when an arm of an <c>and</c> is matched against a type its left neighbour narrowed to.
    /// </summary>
    /// <remarks>
    /// In <c>o is string and { Length: &gt; 0 }</c> the right arm is matched against a
    /// <c>string</c>, not the <c>object</c> the <c>is</c> started from, and alone it reads a
    /// <c>Length</c> an <c>object</c> does not have. The compiler's own operation tree says which
    /// type each arm is matched against. Found in review of #38.
    /// </remarks>
    private static bool ReliesOnTheTypeTheOtherArmNarrows(
        PatternSyntax kept, BinaryPatternSyntax binary, SemanticModel semanticModel) =>
        semanticModel.GetOperation(binary) is IBinaryPatternOperation operation &&
        semanticModel.GetOperation(kept) is IPatternOperation arm &&
        !SymbolEqualityComparer.Default.Equals(arm.InputType, operation.InputType);

    private static bool OnlyRejectsNull(PatternSyntax pattern) => pattern switch
    {
        UnaryPatternSyntax { Pattern: ConstantPatternSyntax constant } not
            when not.IsKind(SyntaxKind.NotPattern) && constant.Expression.IsKind(SyntaxKind.NullLiteralExpression) => true,
        RecursivePatternSyntax { Type: null, PositionalPatternClause: null, Designation: null } recursive
            => recursive.PropertyPatternClause is { Subpatterns.Count: 0 },
        _ => false,
    };

    /// <summary>True when the pattern is part of an <c>is</c> expression rather than of a switch.</summary>
    private static bool BelongsToAnIsExpression(PatternSyntax pattern) =>
        pattern.Ancestors().FirstOrDefault(ancestor =>
            ancestor is ExpressionSyntax or SwitchExpressionArmSyntax or SwitchLabelSyntax) is IsPatternExpressionSyntax;

    /// <summary>True unless the compiler already knows the answer, as it does for a constant.</summary>
    private static bool IsDecidedAtRunTime(ExpressionSyntax test, SemanticModel semanticModel) =>
        !semanticModel.GetConstantValue(test).HasValue;
}
