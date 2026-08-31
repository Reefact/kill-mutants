using Microsoft.CodeAnalysis.CSharp;

namespace KillMutants.Mutations.Mutators;

/// <summary>
/// Replaces an arithmetic operator with a different one.
/// </summary>
/// <remarks>
/// <para>
/// Each operator gets one replacement rather than all four. Arithmetic mutants are cheap to detect
/// when the code is tested at all — almost any assertion on a computed value catches them — so a
/// second mutant per site buys little and costs a full test run.
/// </para>
/// <para>
/// String concatenation needs no special case here: <c>string - string</c> does not exist, so the
/// base class's binding check rejects it along with every other operator that would not compile.
/// </para>
/// </remarks>
internal sealed class ArithmeticOperatorMutator : BinaryOperatorMutator
{
    /// <inheritdoc />
    public override MutatorName Name { get; } = MutatorName.Create("Arithmetic");

    /// <inheritdoc />
    protected override IReadOnlyDictionary<SyntaxKind, IReadOnlyList<SyntaxKind>> Replacements { get; } =
        new Dictionary<SyntaxKind, IReadOnlyList<SyntaxKind>>
        {
            [SyntaxKind.AddExpression] = [SyntaxKind.SubtractExpression],
            [SyntaxKind.SubtractExpression] = [SyntaxKind.AddExpression],
            [SyntaxKind.MultiplyExpression] = [SyntaxKind.DivideExpression],
            [SyntaxKind.DivideExpression] = [SyntaxKind.MultiplyExpression],
            [SyntaxKind.ModuloExpression] = [SyntaxKind.MultiplyExpression],
        };
}
