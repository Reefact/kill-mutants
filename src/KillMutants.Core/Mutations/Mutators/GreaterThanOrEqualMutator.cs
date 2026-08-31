using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KillMutants.Mutations.Mutators;

/// <summary>
/// Turns <c>a &gt;= b</c> into <c>a &gt; b</c>, removing the boundary case.
/// </summary>
/// <remarks>
/// <para>
/// This is the classic off-by-one mutation: a test suite that only ever checks values comfortably
/// inside a range will not notice that the boundary moved.
/// </para>
/// <para>
/// <strong>Why the whole node is replaced.</strong> Replacing only the operator token leaves the
/// parent <see cref="BinaryExpressionSyntax"/> with its original kind,
/// <see cref="SyntaxKind.GreaterThanOrEqualExpression"/>. Roslyn binds and emits from the node kind,
/// not from the token text, so the rewritten tree prints as <c>a &gt; b</c> while the emitted IL is
/// unchanged. The result is a mutant that is silently equivalent to the original and is therefore
/// always reported as survived. This was observed for real during the design of this tool, which is
/// why <c>GreaterThanOrEqualMutatorTests</c> asserts on the resulting node kind and the
/// end-to-end test asserts that the emitted IL actually differs.
/// </para>
/// </remarks>
internal sealed class GreaterThanOrEqualMutator : IMutator
{
    /// <inheritdoc />
    public MutatorName Name { get; } = MutatorName.Create("GreaterThanOrEqual");

    /// <inheritdoc />
    public IEnumerable<MutationCandidate> Mutate(SyntaxNode node)
    {
        if (node is not BinaryExpressionSyntax binary ||
            !binary.IsKind(SyntaxKind.GreaterThanOrEqualExpression))
        {
            yield break;
        }

        yield return new MutationCandidate(Name, binary, ToGreaterThan(binary));
    }

    private static BinaryExpressionSyntax ToGreaterThan(BinaryExpressionSyntax original)
    {
        SyntaxToken greaterThan = SyntaxFactory.Token(
            original.OperatorToken.LeadingTrivia,
            SyntaxKind.GreaterThanToken,
            original.OperatorToken.TrailingTrivia);

        return SyntaxFactory
            .BinaryExpression(SyntaxKind.GreaterThanExpression, original.Left, greaterThan, original.Right)
            .WithTriviaFrom(original);
    }
}
