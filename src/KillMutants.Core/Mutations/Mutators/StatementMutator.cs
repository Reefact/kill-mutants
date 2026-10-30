using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KillMutants.Mutations.Mutators;

/// <summary>
/// Removes a statement that only calls something: <c>Config.Save(merged);</c> becomes <c>{ }</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every other family changes a value or a decision; none asks whether an effect happened at all.
/// Delete <c>Directory.CreateDirectory(path);</c> and no operator has changed, so a suite that never
/// checks the directory exists passes - and only removing the call can say so. DEC0020.
/// </para>
/// <para>
/// Calls only, in this first version. An assignment's removal can leave a local unassigned where it
/// is read (CS0165), and removing a <c>return</c>, a <c>throw</c> or a <c>break</c> changes what the
/// compiler concludes about reachability; both need a flow analysis this family does not make.
/// Stryker.NET refuses assignments for the same reason, and on <c>Reefact/slugger</c> its 139 judged
/// statement mutants were calls. A call that writes an <c>out</c> argument is an assignment in
/// disguise and is refused with them.
/// </para>
/// <para>
/// The replacement is an empty block rather than an empty statement: <c>if (ready) ;</c> draws
/// CS0642, and a block is legal wherever the call was.
/// </para>
/// </remarks>
internal sealed class StatementMutator : IMutator
{
    /// <inheritdoc />
    public MutatorName Name { get; } = MutatorName.Create("Statement");

    /// <inheritdoc />
    public IEnumerable<MutationCandidate> Mutate(SyntaxNode node, SemanticModel semanticModel)
    {
        ArgumentNullException.ThrowIfNull(semanticModel);

        if (node is not ExpressionStatementSyntax statement ||
            !IsACall(statement.Expression) ||
            WritesAnOutArgument(statement) ||
            CallsAConditionalMethod(statement.Expression, semanticModel))
        {
            yield break;
        }

        yield return new MutationCandidate(
            Name,
            statement,
            SyntaxFactory.Block().WithTriviaFrom(statement));
    }

    /// <summary>A call, awaited or not, through <c>?.</c> or not, or an object created for its effect.</summary>
    private static bool IsACall(ExpressionSyntax expression) => expression switch
    {
        AwaitExpressionSyntax awaited => IsACall(awaited.Expression),
        InvocationExpressionSyntax => true,
        ConditionalAccessExpressionSyntax access => IsACall(access.WhenNotNull),
        ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax => true,
        _ => false,
    };

    private static bool WritesAnOutArgument(ExpressionStatementSyntax statement) =>
        statement
            .DescendantNodes(inner => inner is not AnonymousFunctionExpressionSyntax)
            .OfType<ArgumentSyntax>()
            .Any(argument => argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword));

    /// <summary>
    /// True for a call to a method marked <c>[Conditional]</c>, such as <c>Debug.Assert</c>.
    /// </summary>
    /// <remarks>
    /// The compiler drops such a call whenever its symbol is not defined - in a Release build, which
    /// is what a run builds by default - so removing it changes nothing and the mutant could never be
    /// killed: a gap reported in tests that have nothing to find.
    /// </remarks>
    private static bool CallsAConditionalMethod(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        ExpressionSyntax call = expression is AwaitExpressionSyntax awaited ? awaited.Expression : expression;

        return semanticModel.GetSymbolInfo(call).Symbol is IMethodSymbol method &&
               method.GetAttributes().Any(attribute =>
                   attribute.AttributeClass?.ToDisplayString() == "System.Diagnostics.ConditionalAttribute");
    }
}
