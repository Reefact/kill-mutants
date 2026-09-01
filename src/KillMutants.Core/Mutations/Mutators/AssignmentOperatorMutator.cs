using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KillMutants.Mutations.Mutators;

/// <summary>
/// Swaps compound assignment operators: <c>+=</c> becomes <c>-=</c>, and so on.
/// </summary>
/// <remarks>
/// Worth its own family rather than folding into the arithmetic one: a compound assignment is a
/// different syntax node, and it is where accumulation bugs live - a total that adds when it should
/// subtract is exactly the kind of thing a test suite can miss while still exercising the method.
/// </remarks>
internal sealed class AssignmentOperatorMutator : IMutator
{
    private static readonly Dictionary<SyntaxKind, (SyntaxKind Expression, SyntaxKind Token)> Replacements =
        new()
        {
            [SyntaxKind.AddAssignmentExpression] = (SyntaxKind.SubtractAssignmentExpression, SyntaxKind.MinusEqualsToken),
            [SyntaxKind.SubtractAssignmentExpression] = (SyntaxKind.AddAssignmentExpression, SyntaxKind.PlusEqualsToken),
            [SyntaxKind.MultiplyAssignmentExpression] = (SyntaxKind.DivideAssignmentExpression, SyntaxKind.SlashEqualsToken),
            [SyntaxKind.DivideAssignmentExpression] = (SyntaxKind.MultiplyAssignmentExpression, SyntaxKind.AsteriskEqualsToken),
            [SyntaxKind.ModuloAssignmentExpression] = (SyntaxKind.MultiplyAssignmentExpression, SyntaxKind.AsteriskEqualsToken),
            [SyntaxKind.AndAssignmentExpression] = (SyntaxKind.OrAssignmentExpression, SyntaxKind.BarEqualsToken),
            [SyntaxKind.OrAssignmentExpression] = (SyntaxKind.AndAssignmentExpression, SyntaxKind.AmpersandEqualsToken),
            [SyntaxKind.LeftShiftAssignmentExpression] = (SyntaxKind.RightShiftAssignmentExpression, SyntaxKind.GreaterThanGreaterThanEqualsToken),
            [SyntaxKind.RightShiftAssignmentExpression] = (SyntaxKind.LeftShiftAssignmentExpression, SyntaxKind.LessThanLessThanEqualsToken),
        };

    /// <inheritdoc />
    public MutatorName Name { get; } = MutatorName.Create("Assignment");

    /// <inheritdoc />
    public IEnumerable<MutationCandidate> Mutate(SyntaxNode node, SemanticModel semanticModel)
    {
        ArgumentNullException.ThrowIfNull(semanticModel);

        if (node is not AssignmentExpressionSyntax assignment ||
            !Replacements.TryGetValue(assignment.Kind(), out (SyntaxKind Expression, SyntaxKind Token) replacement))
        {
            yield break;
        }

        AssignmentExpressionSyntax rewritten = SyntaxFactory
            .AssignmentExpression(
                replacement.Expression,
                assignment.Left,
                SyntaxFactory.Token(
                    assignment.OperatorToken.LeadingTrivia,
                    replacement.Token,
                    assignment.OperatorToken.TrailingTrivia),
                assignment.Right)
            .WithTriviaFrom(assignment);

        // `text += "x"` has no `-=` counterpart, and a user-defined type may define only one of a
        // pair. Ask the compiler rather than keeping a list of exceptions.
        ITypeSymbol? type = semanticModel.GetSpeculativeTypeInfo(
            assignment.SpanStart, rewritten, SpeculativeBindingOption.BindAsExpression).Type;

        if (type is not null && type.TypeKind != TypeKind.Error)
        {
            yield return new MutationCandidate(Name, assignment, rewritten);
        }
    }
}
