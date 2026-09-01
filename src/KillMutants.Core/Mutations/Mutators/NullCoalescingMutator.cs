using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KillMutants.Mutations.Mutators;

/// <summary>Drops the fallback of a null-coalescing expression: <c>a ?? b</c> becomes <c>a</c>.</summary>
/// <remarks>
/// A surviving mutant here says no test ever reaches the expression with a null left operand, so the
/// fallback is unverified - one of the cheapest ways to find a default nobody exercises. The mirror
/// mutation, keeping only <c>b</c>, is deliberately not produced: it discards the left operand and
/// with it any side effect, which turns a missing-coverage signal into an unrelated behaviour change.
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
            !FitsWhereTheWholeExpressionStood(coalesce, semanticModel))
        {
            yield break;
        }

        yield return new MutationCandidate(Name, coalesce, coalesce.Left.WithTriviaFrom(coalesce));
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
    private static bool FitsWhereTheWholeExpressionStood(
        BinaryExpressionSyntax coalesce,
        SemanticModel semanticModel)
    {
        if (semanticModel.GetTypeInfo(coalesce).ConvertedType is not { } expected ||
            expected.TypeKind == TypeKind.Error)
        {
            return false;
        }

        Conversion conversion = semanticModel.ClassifyConversion(coalesce.Left, expected);

        return conversion.Exists && conversion.IsImplicit;
    }
}
