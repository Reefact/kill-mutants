using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KillMutants.Mutations.Mutators;

/// <summary>
/// Removes a statement that only calls something, <c>Config.Save(merged);</c>, or that yields one
/// element of an iterator, <c>yield return token;</c>: either becomes <c>{ }</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every other family changes a value or a decision; none asks whether an effect happened at all.
/// Delete <c>Directory.CreateDirectory(path);</c> and no operator has changed, so a suite that never
/// checks the directory exists passes - and only removing the call can say so. DEC0020.
/// </para>
/// <para>
/// Calls and <c>yield return</c>, and nothing else. An assignment's removal can leave a local
/// unassigned where it is read (CS0165), and removing a <c>return</c>, a <c>throw</c>, a <c>break</c>
/// or a <c>yield break</c> changes what the compiler concludes about reachability; both need a flow
/// analysis this family does not make. Stryker.NET refuses assignments for the same reason, and on
/// <c>Reefact/slugger</c> its 139 judged statement mutants were calls. A call or a <c>yield</c>
/// whose expression assigns a variable - <c>f(x = 1);</c>, <c>f(out x);</c>,
/// <c>yield return n = 1;</c> - is an assignment in disguise and is refused with them: removing it
/// can leave the variable unassigned where it is read next (CS0165). A compound assignment or an
/// increment needs the variable assigned already, so <c>yield return items[i++];</c> is removed.
/// </para>
/// <para>
/// A <c>yield return</c> is the one control-flow statement whose removal needs no such analysis:
/// the element is skipped and the iterator runs on, well-formed, as long as another <c>yield</c>
/// keeps the method an iterator. DEC0020 drew its line at reachability, not at calls as such, so
/// the statement is on the calls' side of it. Removing the only <c>yield return</c> of an iterator
/// is the exception: the method stops being one and no longer returns a value on every path
/// (CS0161), so that mutant is not proposed at all rather than left to the compiler to refuse. On
/// <c>Reefact/first-class-errors</c> the two tokens a security analyzer's tokenizer yielded before a
/// separator and before a camel-case hump could both be dropped without a test noticing (#62), and no
/// family had a mutant on either line.
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

        if (node is not StatementSyntax statement ||
            !(IsACallStatement(statement, semanticModel) || IsAYieldTheIteratorCanLose(statement)))
        {
            yield break;
        }

        yield return new MutationCandidate(
            Name,
            statement,
            SyntaxFactory.Block().WithTriviaFrom(statement));
    }

    private static bool IsACallStatement(StatementSyntax statement, SemanticModel semanticModel) =>
        statement is ExpressionStatementSyntax call &&
        IsACall(call.Expression) &&
        !AssignsAVariable(call) &&
        !CallsAMethodTheCompilerDrops(call.Expression, semanticModel);

    /// <summary>
    /// A <c>yield return</c> that is not the only one of its iterator, so that removing it leaves
    /// the method an iterator.
    /// </summary>
    /// <remarks>
    /// The iterator is the nearest member, accessor or local function; a local function nested in
    /// it is an iterator of its own, so its <c>yield</c>s count for it and not for the outer one. A
    /// <c>yield break</c> keeps a method an iterator too, but it is not what a reader would call
    /// one element among others, and a method that only breaks has nothing to yield: the count is of
    /// <c>yield return</c> alone, which is the stricter rule.
    /// </remarks>
    private static bool IsAYieldTheIteratorCanLose(StatementSyntax statement)
    {
        if (!statement.IsKind(SyntaxKind.YieldReturnStatement) || AssignsAVariable(statement))
        {
            return false;
        }

        SyntaxNode? iterator = statement.Ancestors().FirstOrDefault(ancestor =>
            ancestor is MemberDeclarationSyntax
                or AccessorDeclarationSyntax
                or LocalFunctionStatementSyntax
                or AnonymousFunctionExpressionSyntax);

        // The descent predicate is asked of the iterator itself, which may be a local function: it
        // is let through, and only the functions nested in it are not.
        return iterator is not null &&
               iterator
                   .DescendantNodes(inner =>
                       inner == iterator ||
                       inner is not (LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax))
                   .Count(inner => inner.IsKind(SyntaxKind.YieldReturnStatement)) > 1;
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

    /// <summary>
    /// True when the statement assigns a variable with <c>=</c> or through an <c>out</c> argument,
    /// outside any lambda it holds. Review of #69 found <c>yield return n = 1;</c>, whose removal
    /// leaves <c>n</c> unassigned at the next <c>yield return n;</c>.
    /// </summary>
    private static bool AssignsAVariable(StatementSyntax statement) =>
        statement
            .DescendantNodes(inner => inner is not AnonymousFunctionExpressionSyntax)
            .Any(inner =>
                inner.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                (inner is ArgumentSyntax argument && argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword)));

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

    /// <summary>The invocation a call statement makes, through <c>await</c> and <c>?.</c>.</summary>
    private static InvocationExpressionSyntax? Invocation(ExpressionSyntax expression) => expression switch
    {
        AwaitExpressionSyntax awaited => Invocation(awaited.Expression),
        ConditionalAccessExpressionSyntax access => Invocation(access.WhenNotNull),
        InvocationExpressionSyntax invocation => invocation,
        _ => null,
    };
}
