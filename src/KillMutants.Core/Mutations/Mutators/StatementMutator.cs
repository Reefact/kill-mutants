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
            CallsAMethodTheCompilerDrops(statement.Expression, semanticModel) ||
            OnlyPermitsConcurrentAnalysis(statement.Expression, semanticModel))
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
    /// True for a call the compiler removes by itself: to a method marked <c>[Conditional]</c>, such
    /// as <c>Debug.Assert</c>, or to a partial method nobody implemented.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <c>[Conditional]</c> call is dropped whenever its symbol is not defined - in a Release build,
    /// which is what a run builds by default - and a call to a partial method with no implementation
    /// is dropped always. Removing either changes nothing, so the mutant could never be killed: a gap
    /// reported in tests that have nothing to find.
    /// </para>
    /// <para>
    /// The method is asked of the invocation itself, inside <c>await</c> and inside <c>?.</c>: the
    /// conditional access as a whole binds to no method, which let <c>log?.Trace()</c> through.
    /// </para>
    /// </remarks>
    private static bool CallsAMethodTheCompilerDrops(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        if (Invocation(expression) is not { } invocation ||
            semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
        {
            return false;
        }

        return method is { IsPartialDefinition: true, PartialImplementationPart: null } ||
               method.GetAttributes().Any(attribute =>
                   attribute.AttributeClass?.ToDisplayString() == "System.Diagnostics.ConditionalAttribute");
    }

    /// <summary>
    /// True for <c>AnalysisContext.EnableConcurrentExecution()</c>, the first line of nearly every
    /// Roslyn analyzer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The call lets the host run an analyzer's callbacks in parallel; an analyzer has to report the
    /// same diagnostics either way, so no assertion on what it reports can see the call go. On
    /// <c>Reefact/first-class-errors</c> its removal survived in all 22 analyzers, a quarter of the
    /// family's survivors there, and not one was a gap (#49).
    /// </para>
    /// <para>
    /// <c>ConfigureGeneratedCodeAnalysis</c>, the line below it, keeps its mutant: without it the
    /// analyzer reports in generated code too, which a test feeding an <c>auto-generated</c> file
    /// sees. Bound through the compiler, overrides included, so a method of the same name elsewhere
    /// is still removed.
    /// </para>
    /// </remarks>
    private static bool OnlyPermitsConcurrentAnalysis(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        if (Invocation(expression) is not { } invocation ||
            semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method ||
            method.Name != "EnableConcurrentExecution")
        {
            return false;
        }

        for (IMethodSymbol? candidate = method; candidate is not null; candidate = candidate.OverriddenMethod)
        {
            if (candidate.ContainingType.ToDisplayString() == "Microsoft.CodeAnalysis.Diagnostics.AnalysisContext")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The invocation a call statement makes, through <c>await</c> and <c>?.</c>.</summary>
    private static InvocationExpressionSyntax? Invocation(ExpressionSyntax expression) => expression switch
    {
        AwaitExpressionSyntax awaited => Invocation(awaited.Expression),
        ConditionalAccessExpressionSyntax access => Invocation(access.WhenNotNull),
        InvocationExpressionSyntax invocation => invocation,
        _ => null,
    };
}
