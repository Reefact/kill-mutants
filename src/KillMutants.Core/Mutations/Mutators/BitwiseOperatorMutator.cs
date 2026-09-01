using Microsoft.CodeAnalysis.CSharp;

namespace KillMutants.Mutations.Mutators;

/// <summary>
/// Swaps bitwise and non-short-circuiting logical operators.
/// </summary>
/// <remarks>
/// On integers these change the arithmetic; on booleans <c>&amp;</c> and <c>|</c> are the
/// non-short-circuiting forms of <c>&amp;&amp;</c> and <c>||</c>, so the same rewrite covers both
/// without needing to know which it is - the base class asks the compiler whether the replacement
/// binds, and a type that defines only one of a pair is rejected there.
/// </remarks>
internal sealed class BitwiseOperatorMutator : BinaryOperatorMutator
{
    /// <inheritdoc />
    public override MutatorName Name { get; } = MutatorName.Create("Bitwise");

    /// <inheritdoc />
    protected override IReadOnlyDictionary<SyntaxKind, IReadOnlyList<SyntaxKind>> Replacements { get; } =
        new Dictionary<SyntaxKind, IReadOnlyList<SyntaxKind>>
        {
            [SyntaxKind.BitwiseAndExpression] = [SyntaxKind.BitwiseOrExpression],
            [SyntaxKind.BitwiseOrExpression] = [SyntaxKind.BitwiseAndExpression],
            [SyntaxKind.ExclusiveOrExpression] = [SyntaxKind.BitwiseAndExpression],
            [SyntaxKind.LeftShiftExpression] = [SyntaxKind.RightShiftExpression],
            [SyntaxKind.RightShiftExpression] = [SyntaxKind.LeftShiftExpression],
        };
}
