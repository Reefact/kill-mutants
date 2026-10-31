using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KillMutants.Mutations.Mutators;

/// <summary>Swaps <c>true</c> and <c>false</c>.</summary>
/// <remarks>
/// <para>
/// Blunt, and effective for exactly that reason: a literal nobody's test depends on is a literal
/// nobody's test is checking.
/// </para>
/// <para>
/// Except the argument of <c>ConfigureAwait</c>. It decides whether the continuation goes back to
/// the captured <c>SynchronizationContext</c>, which a test can only see under a context that runs
/// posted work somewhere it can tell apart, or that deadlocks when the caller blocks - and test code
/// runs under neither. On <c>Reefact/first-class-errors</c> all 42 such mutants survived, 61% of the
/// family's survivors, and none said anything about the library's tests (#48). Bound through the
/// compiler, so a method of the same name on another type keeps its mutant, and only the
/// <see langword="bool"/> overloads: <c>ConfigureAwaitOptions</c> carries choices a test can see.
/// </para>
/// </remarks>
internal sealed class BooleanLiteralMutator : IMutator
{
    /// <inheritdoc />
    public MutatorName Name { get; } = MutatorName.Create("BooleanLiteral");

    /// <inheritdoc />
    public IEnumerable<MutationCandidate> Mutate(SyntaxNode node, SemanticModel semanticModel)
    {
        if (node is not LiteralExpressionSyntax literal)
        {
            yield break;
        }

        SyntaxKind replacement = literal.Kind() switch
        {
            SyntaxKind.TrueLiteralExpression => SyntaxKind.FalseLiteralExpression,
            SyntaxKind.FalseLiteralExpression => SyntaxKind.TrueLiteralExpression,
            _ => SyntaxKind.None,
        };

        if (replacement == SyntaxKind.None || IsTheArgumentOfConfigureAwait(literal, semanticModel))
        {
            yield break;
        }

        SyntaxToken token = SyntaxFactory.Token(
            literal.Token.LeadingTrivia,
            replacement == SyntaxKind.TrueLiteralExpression ? SyntaxKind.TrueKeyword : SyntaxKind.FalseKeyword,
            literal.Token.TrailingTrivia);

        yield return new MutationCandidate(
            Name,
            literal,
            SyntaxFactory.LiteralExpression(replacement, token).WithTriviaFrom(literal));
    }

    /// <summary>
    /// True for the <see langword="bool"/> passed to a <c>ConfigureAwait</c> of the task-like types in
    /// <c>System.Threading.Tasks</c> or <c>System.Runtime.CompilerServices</c>.
    /// </summary>
    private static bool IsTheArgumentOfConfigureAwait(LiteralExpressionSyntax literal, SemanticModel semanticModel)
    {
        ArgumentNullException.ThrowIfNull(semanticModel);

        if (literal.Parent is not ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax invocation } } ||
            semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
        {
            return false;
        }

        IMethodSymbol definition = method.ReducedFrom ?? method;
        string? owner = definition.ContainingNamespace?.ToDisplayString();

        return definition.Name == "ConfigureAwait" &&
               definition.Parameters is [.., { Type.SpecialType: SpecialType.System_Boolean }] &&
               owner is "System.Threading.Tasks" or "System.Runtime.CompilerServices";
    }
}
