using Microsoft.CodeAnalysis.CSharp;

namespace KillMutants.Mutations.Mutators;

/// <summary>
/// Rewrites relational and equality operators.
/// </summary>
/// <remarks>
/// <para>
/// Each relational operator produces exactly two mutants, chosen so that they ask different
/// questions of the test suite:
/// </para>
/// <list type="bullet">
///   <item>
///     the <em>boundary</em> shift (<c>&gt;=</c> becomes <c>&gt;</c>), which survives unless a test
///     exercises the edge of the range - the classic off-by-one;
///   </item>
///   <item>
///     the <em>negation</em> (<c>&gt;=</c> becomes <c>&lt;</c>), which survives only if the
///     condition is barely tested at all.
///   </item>
/// </list>
/// <para>
/// Generating all five alternatives per operator was rejected: the extra three are largely
/// subsumed by these two, and every additional mutant costs a full test run.
/// </para>
/// </remarks>
internal sealed class ComparisonOperatorMutator : BinaryOperatorMutator
{
    /// <inheritdoc />
    public override MutatorName Name { get; } = MutatorName.Create("Comparison");

    /// <inheritdoc />
    protected override IReadOnlyDictionary<SyntaxKind, IReadOnlyList<SyntaxKind>> Replacements { get; } =
        new Dictionary<SyntaxKind, IReadOnlyList<SyntaxKind>>
        {
            //                                     boundary                              negation
            [SyntaxKind.LessThanExpression] = [SyntaxKind.LessThanOrEqualExpression, SyntaxKind.GreaterThanOrEqualExpression],
            [SyntaxKind.LessThanOrEqualExpression] = [SyntaxKind.LessThanExpression, SyntaxKind.GreaterThanExpression],
            [SyntaxKind.GreaterThanExpression] = [SyntaxKind.GreaterThanOrEqualExpression, SyntaxKind.LessThanOrEqualExpression],
            [SyntaxKind.GreaterThanOrEqualExpression] = [SyntaxKind.GreaterThanExpression, SyntaxKind.LessThanExpression],
            [SyntaxKind.EqualsExpression] = [SyntaxKind.NotEqualsExpression],
            [SyntaxKind.NotEqualsExpression] = [SyntaxKind.EqualsExpression],
        };
}
