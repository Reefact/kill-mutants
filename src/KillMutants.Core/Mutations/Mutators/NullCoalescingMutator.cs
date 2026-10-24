using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KillMutants.Mutations.Mutators;

/// <summary>
/// Drops the fallback of a null-coalescing expression, <c>a ?? b</c> becoming <c>a</c>, and swaps
/// its operands, <c>a ?? b</c> becoming <c>b ?? a</c>, where the swap can be asked honestly.
/// </summary>
/// <remarks>
/// <para>
/// A surviving mutant of the first kind says no test ever reaches the expression with a null left
/// operand, so the fallback is unverified - one of the cheapest ways to find a default nobody
/// exercises. The mirror mutation, keeping only <c>b</c>, is deliberately not produced: it discards
/// the left operand and with it any side effect, which turns a missing-coverage signal into an
/// unrelated behaviour change.
/// </para>
/// <para>
/// The swap asks what neither of those can: which operand wins when both are set. Measured on
/// Reefact/slugger (#17), whose options resolve through a chain of
/// <c>commandLine.X ?? saved.X</c>: Stryker's swap survived on thirteen of fifteen of them, because
/// nothing asserted that a flag beat a saved setting, and the tests written to kill them found a
/// real precedence defect. Dropping the fallback could not have pointed at it - the same tests
/// kill that mutant trivially.
/// </para>
/// </remarks>
internal sealed class NullCoalescingMutator : IMutator
{
    /// <inheritdoc />
    public MutatorName Name { get; } = MutatorName.Create("NullCoalescing");

    /// <inheritdoc />
    public IEnumerable<MutationCandidate> Mutate(SyntaxNode node, SemanticModel semanticModel)
    {
        ArgumentNullException.ThrowIfNull(semanticModel);

        if (node is not BinaryExpressionSyntax coalesce ||
            !coalesce.IsKind(SyntaxKind.CoalesceExpression) ||
            semanticModel.GetTypeInfo(coalesce).ConvertedType is not { } expected ||
            expected.TypeKind == TypeKind.Error)
        {
            yield break;
        }

        if (LeftFitsWhereTheWholeExpressionStood(coalesce, expected, semanticModel))
        {
            yield return new MutationCandidate(
                Name, coalesce, coalesce.Left.WithTriviaFrom(coalesce));
        }

        if (!CanBeSwapped(coalesce))
        {
            yield break;
        }

        BinaryExpressionSyntax swapped = Swapped(coalesce);

        if (SwapFits(swapped, coalesce, expected, semanticModel))
        {
            yield return new MutationCandidate(Name, coalesce, swapped);
        }
    }

    /// <summary>
    /// True when the left operand on its own converts to whatever the surrounding code expected.
    /// </summary>
    /// <remarks>
    /// The point of <c>??</c> is often to remove nullability, and there the left operand does not fit:
    /// <c>int total = count ?? 0</c> mutated to <c>int total = count</c> is a hard error, and
    /// <c>string s = name ?? ""</c> only survives because the nullable warning is not an error here.
    /// Classifying the conversion the compiler would have to make answers both cases in one rule,
    /// including the widening ones - <c>object o = text ?? fallback</c> stays mutable.
    /// </remarks>
    private static bool LeftFitsWhereTheWholeExpressionStood(
        BinaryExpressionSyntax coalesce,
        ITypeSymbol expected,
        SemanticModel semanticModel)
    {
        Conversion conversion = semanticModel.ClassifyConversion(coalesce.Left, expected);

        return conversion.Exists && conversion.IsImplicit;
    }

    /// <summary>True when the swap asks a question and loses nothing to ask it.</summary>
    /// <remarks>
    /// <para>
    /// The left operand has to be one that can do nothing but read. <c>b ?? a</c> evaluates
    /// <c>a</c> only when <c>b</c> is null, so swapping <c>Advance() ?? fallback</c> would skip the
    /// call whenever the fallback is set - the side effect the first mutation refuses to drop,
    /// dropped by the second. A property is taken at its word as a read; a call, an index or an
    /// assignment is not.
    /// </para>
    /// <para>
    /// Three right operands are refused for what they would make of the mutant. The same expression
    /// on both sides swaps into the original. A <c>null</c> or <c>default</c> fallback swaps into
    /// <c>null ?? a</c>, which is <c>a</c> - the mutant the first rule already produced, judged
    /// twice. And a right operand that is itself a <c>??</c> or a <c>throw</c> cannot stand on the
    /// left as it is: <c>x ?? y ?? a</c> reads back as <c>x ?? (y ?? a)</c>, so the report would
    /// show a mutant that is not the one compiled.
    /// </para>
    /// <para>
    /// Nothing on the left is refused too, and review found why: <c>null ?? s</c> and
    /// <c>s ?? null</c> are both <c>s</c>, null or not, so the swap is a mutant no test could kill.
    /// It compiles to different instructions all the same, which is why the corpus that compares
    /// emitted programs cannot see it and the rule has to.
    /// </para>
    /// </remarks>
    private static bool CanBeSwapped(BinaryExpressionSyntax coalesce) =>
        OnlyReads(coalesce.Left) &&
        !IsNothing(coalesce.Left) &&
        !IsNothing(coalesce.Right) &&
        !coalesce.Right.IsKind(SyntaxKind.CoalesceExpression) &&
        !coalesce.Right.IsKind(SyntaxKind.ThrowExpression) &&
        !SyntaxFactory.AreEquivalent(coalesce.Left, coalesce.Right);

    /// <summary>True for an operand that can only be null: <c>null</c> or a default.</summary>
    private static bool IsNothing(ExpressionSyntax operand) =>
        operand.IsKind(SyntaxKind.NullLiteralExpression) ||
        operand.IsKind(SyntaxKind.DefaultLiteralExpression) ||
        operand.IsKind(SyntaxKind.DefaultExpression);

    /// <summary>True when evaluating the expression can only read a value.</summary>
    private static bool OnlyReads(ExpressionSyntax expression) => expression switch
    {
        IdentifierNameSyntax or ThisExpressionSyntax or LiteralExpressionSyntax => true,
        MemberAccessExpressionSyntax member
            when member.IsKind(SyntaxKind.SimpleMemberAccessExpression) =>
            OnlyReads(member.Expression),
        ParenthesizedExpressionSyntax parenthesized => OnlyReads(parenthesized.Expression),
        _ => false,
    };

    /// <summary>
    /// The two operands exchanged, as a new coalesce node - the RB-001 rule, for a family that
    /// swaps operands rather than operators.
    /// </summary>
    private static BinaryExpressionSyntax Swapped(BinaryExpressionSyntax coalesce) =>
        SyntaxFactory
            .BinaryExpression(
                SyntaxKind.CoalesceExpression,
                coalesce.Right.WithTriviaFrom(coalesce.Left),
                coalesce.OperatorToken,
                coalesce.Left.WithTriviaFrom(coalesce.Right))
            .WithTriviaFrom(coalesce);

    /// <summary>
    /// True when the swapped expression binds, and converts to what the original stood for.
    /// </summary>
    /// <remarks>
    /// The swap has a type question of its own, and it is the reverse of the first mutation's. The
    /// left operand of <c>??</c> has to be able to be null, so <c>count ?? 0</c> cannot become
    /// <c>0 ?? count</c> at all: the compiler refuses it, and asking it is what keeps the mutant
    /// from being generated (RB-011). What binds then has to fit where the original stood, by the
    /// same conversion rule as the first mutation.
    /// </remarks>
    private static bool SwapFits(
        BinaryExpressionSyntax swapped,
        BinaryExpressionSyntax original,
        ITypeSymbol expected,
        SemanticModel semanticModel)
    {
        ITypeSymbol? type = semanticModel.GetSpeculativeTypeInfo(
            original.SpanStart, swapped, SpeculativeBindingOption.BindAsExpression).Type;

        if (type is null || type.TypeKind == TypeKind.Error)
        {
            return false;
        }

        Conversion conversion = semanticModel.Compilation.ClassifyConversion(type, expected);

        return conversion.Exists && conversion.IsImplicit;
    }
}
