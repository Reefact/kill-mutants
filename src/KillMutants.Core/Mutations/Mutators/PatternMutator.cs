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
                [MutationSite.IsInsideAnExpressionTree(test, semanticModel) ? NegatedAsAnOperator(test) : NegatedTypeTest(test, type, semanticModel)],
            RelationalPatternSyntax relational when BelongsToAnIsExpression(relational) => Shifted(relational),
            BinaryPatternSyntax binary when BelongsToAnIsExpression(binary) => Arms(binary, semanticModel),
            _ => [],
        };

        foreach (SyntaxNode replacement in replacements)
        {
            SyntaxNode mutated = replacement.WithTriviaFrom(node);

            if (Binds(node, mutated, semanticModel))
            {
                yield return new MutationCandidate(Name, node, mutated);
            }
        }
    }

    /// <summary>True when the member holding the mutant still compiles - the RB-011 rule.</summary>
    /// <remarks>
    /// A pattern can bind and still be refused: dropping <c>"a"</c> from
    /// <c>s is not null and (null or "a")</c> leaves <c>not null and null</c>, which the compiler
    /// rejects as a pattern that never matches. That is decided over the whole pattern rather than
    /// the node, so the member is rebound with the mutant in place and its errors are read. Found in
    /// review of #38. Patterns are few, which is what makes binding a member per mutant affordable.
    /// </remarks>
    private static bool Binds(SyntaxNode original, SyntaxNode mutated, SemanticModel semanticModel)
    {
        SyntaxAnnotation marker = new();
        SyntaxTree tree = original.SyntaxTree;
        SyntaxNode root = tree.GetRoot().ReplaceNode(original, mutated.WithAdditionalAnnotations(marker));
        SyntaxTree rewritten = tree.WithRootAndOptions(root, tree.Options);
        SemanticModel model = semanticModel.Compilation
            .ReplaceSyntaxTree(tree, rewritten)
            .GetSemanticModel(rewritten);

        SyntaxNode placed = rewritten.GetRoot().GetAnnotatedNodes(marker).Single();
        SyntaxNode scope = placed.FirstAncestorOrSelf<MemberDeclarationSyntax>() ?? rewritten.GetRoot();

        return !model.GetDiagnostics(scope.Span).Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
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
    /// An <c>and</c> keeps its <c>not null</c> or empty <c>{ }</c> when the arm beside it rejects
    /// <c>null</c> already, as <c>x is not null and { Length: &gt; 0 }</c> does: the mutant would
    /// behave exactly like the original.
    /// </para>
    /// </remarks>
    private static IEnumerable<PatternSyntax> Arms(BinaryPatternSyntax binary, SemanticModel semanticModel)
    {
        foreach ((PatternSyntax kept, PatternSyntax dropped) in new[] { (binary.Left, binary.Right), (binary.Right, binary.Left) })
        {
            if (binary.IsKind(SyntaxKind.AndPattern) &&
                ((OnlyRejectsNull(dropped, semanticModel) && !MayMatchNull(kept, semanticModel)) ||
                 ReliesOnTheTypeTheOtherArmNarrows(kept, binary, semanticModel)))
            {
                continue;
            }

            if (binary.IsKind(SyntaxKind.OrPattern) && AlreadyMatchesEverything(kept, dropped, semanticModel))
            {
                continue;
            }

            yield return kept.WithoutTrivia();
        }
    }

    /// <summary>
    /// True when the arm an <c>or</c> keeps is a type test that every value of the dropped arm's type
    /// passes.
    /// </summary>
    /// <remarks>
    /// <c>e is NotSupportedException or PlatformNotSupportedException</c> as
    /// <c>e is NotSupportedException</c> matches exactly what it matched before: the second type
    /// derives from the first. On <c>Reefact/first-class-errors</c> that mutant survived in a
    /// <c>catch</c> filter and could not do otherwise (#50). The redundancy is the source's, and a
    /// survivor is the wrong way to say so. Asked of the compiler's operation tree, which binds a name
    /// written as a constant pattern to the type it is. Only plain type tests: a declaration
    /// pattern's variable, or a property pattern, makes the arm say more than its type.
    /// </remarks>
    private static bool AlreadyMatchesEverything(PatternSyntax kept, PatternSyntax dropped, SemanticModel semanticModel)
    {
        if (semanticModel.GetOperation(kept) is not ITypePatternOperation keptTest ||
            semanticModel.GetOperation(dropped) is not ITypePatternOperation droppedTest)
        {
            return false;
        }

        CommonConversion conversion = semanticModel.Compilation.ClassifyCommonConversion(droppedTest.MatchedType, keptTest.MatchedType);

        return conversion.IsIdentity || (conversion.IsImplicit && conversion.IsReference);
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

    /// <summary>
    /// False only when the pattern certainly rejects <c>null</c>; true when it may match it.
    /// </summary>
    /// <remarks>
    /// The <c>not null</c> of an <c>and</c> is only redundant when the arm beside it rejects
    /// <c>null</c> itself. <c>s is not null and (null or "a")</c> matches <c>"a"</c> alone, and
    /// without its null check it matches <c>null</c> too - a mutant that asks exactly whether a
    /// test passes <c>null</c>. Found in review of #38. Read from the pattern's shape and its
    /// constants' values, and conservative: a shape this does not know may match <c>null</c>, so its
    /// mutant is kept.
    /// </remarks>
    private static bool MayMatchNull(PatternSyntax pattern, SemanticModel semanticModel) => pattern switch
    {
        ConstantPatternSyntax constant => IsNull(constant, semanticModel),
        TypePatternSyntax or DeclarationPatternSyntax or RecursivePatternSyntax or RelationalPatternSyntax
            or ListPatternSyntax => false,
        UnaryPatternSyntax negation when negation.IsKind(SyntaxKind.NotPattern) => !MayRejectNull(negation.Pattern, semanticModel),
        BinaryPatternSyntax either when either.IsKind(SyntaxKind.OrPattern) =>
            MayMatchNull(either.Left, semanticModel) || MayMatchNull(either.Right, semanticModel),
        BinaryPatternSyntax both when both.IsKind(SyntaxKind.AndPattern) =>
            MayMatchNull(both.Left, semanticModel) && MayMatchNull(both.Right, semanticModel),
        ParenthesizedPatternSyntax parenthesized => MayMatchNull(parenthesized.Pattern, semanticModel),
        _ => true,
    };

    /// <summary>The other half of <see cref="MayMatchNull"/>: false only when the pattern certainly matches <c>null</c>.</summary>
    private static bool MayRejectNull(PatternSyntax pattern, SemanticModel semanticModel) => pattern switch
    {
        ConstantPatternSyntax constant => !IsNull(constant, semanticModel),
        VarPatternSyntax or DiscardPatternSyntax => false,
        UnaryPatternSyntax negation when negation.IsKind(SyntaxKind.NotPattern) => MayMatchNull(negation.Pattern, semanticModel),
        BinaryPatternSyntax either when either.IsKind(SyntaxKind.OrPattern) =>
            MayRejectNull(either.Left, semanticModel) && MayRejectNull(either.Right, semanticModel),
        BinaryPatternSyntax both when both.IsKind(SyntaxKind.AndPattern) =>
            MayRejectNull(both.Left, semanticModel) || MayRejectNull(both.Right, semanticModel),
        ParenthesizedPatternSyntax parenthesized => MayRejectNull(parenthesized.Pattern, semanticModel),
        _ => true,
    };

    private static bool OnlyRejectsNull(PatternSyntax pattern, SemanticModel semanticModel) => pattern switch
    {
        UnaryPatternSyntax { Pattern: ConstantPatternSyntax constant } not
            when not.IsKind(SyntaxKind.NotPattern) && IsNull(constant, semanticModel) => true,
        RecursivePatternSyntax { Type: null, PositionalPatternClause: null, Designation: null } recursive
            => recursive.PropertyPatternClause is { Subpatterns.Count: 0 },
        _ => false,
    };

    /// <summary>
    /// True when the constant is <c>null</c>, however it is written.
    /// </summary>
    /// <remarks>
    /// Asked of the compiler rather than the syntax: <c>const string N = null;</c> makes
    /// <c>s is N</c> a null test the literal alone would miss, and with it <c>not null and (N or "a")</c>
    /// would lose a mutant that changes what matches. Found in review of #38.
    /// </remarks>
    private static bool IsNull(ConstantPatternSyntax constant, SemanticModel semanticModel) =>
        semanticModel.GetConstantValue(constant.Expression) is { HasValue: true, Value: null };

    /// <summary>True when the pattern is part of an <c>is</c> expression rather than of a switch.</summary>
    private static bool BelongsToAnIsExpression(PatternSyntax pattern) =>
        pattern.Ancestors().FirstOrDefault(ancestor =>
            ancestor is ExpressionSyntax or SwitchExpressionArmSyntax or SwitchLabelSyntax) is IsPatternExpressionSyntax;

    /// <summary>True unless the compiler already knows the answer, as it does for a constant.</summary>
    private static bool IsDecidedAtRunTime(ExpressionSyntax test, SemanticModel semanticModel) =>
        !semanticModel.GetConstantValue(test).HasValue;
}
