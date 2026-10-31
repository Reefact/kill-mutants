using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace KillMutants.Mutations.Mutators;

/// <summary>
/// Empties a string literal, or fills an empty one.
/// </summary>
/// <remarks>
/// <para>
/// A surviving mutant here says the text was never asserted on — a common gap in code that builds
/// messages, keys or paths. Only ordinary literals are touched: interpolated and verbatim-with-content
/// forms are left for a later milestone, and literals in places the compiler folds into callers are
/// already excluded by <see cref="MutationSite"/>.
/// </para>
/// <para>
/// The name given to an expression tree's parameter, variable or label is left alone. It shows in a
/// debug view and in <c>ToString()</c>, and a compiled lambda behaves the same without it, so the
/// mutant could never be killed; on <c>Reefact/first-class-errors</c> both such mutants survived
/// (#50).
/// </para>
/// </remarks>
internal sealed class StringLiteralMutator : IMutator
{
    private const string NonEmptyReplacement = "KillMutants";

    /// <inheritdoc />
    public MutatorName Name { get; } = MutatorName.Create("StringLiteral");

    /// <inheritdoc />
    public IEnumerable<MutationCandidate> Mutate(SyntaxNode node, SemanticModel semanticModel)
    {
        if (node is not LiteralExpressionSyntax literal ||
            !literal.IsKind(SyntaxKind.StringLiteralExpression) ||
            literal.Token.ValueText is not { } text ||
            NamesAnExpressionTreeNode(literal, semanticModel))
        {
            yield break;
        }

        string replacement = text.Length == 0 ? NonEmptyReplacement : string.Empty;

        yield return new MutationCandidate(
            Name,
            literal,
            SyntaxFactory
                .LiteralExpression(
                    SyntaxKind.StringLiteralExpression,
                    SyntaxFactory.Literal(replacement))
                .WithTriviaFrom(literal));
    }

    /// <summary>
    /// True for the <c>name</c> argument of <c>Expression.Parameter</c>, <c>Expression.Variable</c>
    /// or <c>Expression.Label</c>, asked of the compiler so a method of the same name elsewhere keeps
    /// its mutant.
    /// </summary>
    private static bool NamesAnExpressionTreeNode(LiteralExpressionSyntax literal, SemanticModel semanticModel)
    {
        ArgumentNullException.ThrowIfNull(semanticModel);

        return literal.Parent is ArgumentSyntax argument &&
               semanticModel.GetOperation(argument) is IArgumentOperation { Parameter: { Name: "name" } parameter } &&
               parameter.ContainingSymbol is IMethodSymbol { Name: "Parameter" or "Variable" or "Label" } method &&
               method.ContainingType.ToDisplayString() == "System.Linq.Expressions.Expression";
    }
}
