using Microsoft.CodeAnalysis.CSharp;

namespace KillMutants.Mutations.Mutators;

/// <summary>
/// Swaps <c>&amp;&amp;</c> and <c>||</c>.
/// </summary>
/// <remarks>
/// A test suite that only ever exercises inputs where both operands agree cannot tell the two apart,
/// which is precisely the gap worth reporting. Note that this also changes short-circuiting, so a
/// surviving mutant here can additionally mean a side effect in the right-hand operand is untested.
/// </remarks>
internal sealed class LogicalOperatorMutator : BinaryOperatorMutator
{
    /// <inheritdoc />
    public override MutatorName Name { get; } = MutatorName.Create("LogicalOperator");

    /// <inheritdoc />
    protected override IReadOnlyDictionary<SyntaxKind, IReadOnlyList<SyntaxKind>> Replacements { get; } =
        new Dictionary<SyntaxKind, IReadOnlyList<SyntaxKind>>
        {
            [SyntaxKind.LogicalAndExpression] = [SyntaxKind.LogicalOrExpression],
            [SyntaxKind.LogicalOrExpression] = [SyntaxKind.LogicalAndExpression],
        };
}
