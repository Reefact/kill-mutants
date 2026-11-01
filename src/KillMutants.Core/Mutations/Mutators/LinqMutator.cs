using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KillMutants.Mutations.Mutators;

/// <summary>
/// Swaps a call for its counterpart - <c>xs.Any(p)</c> becomes <c>xs.All(p)</c>, <c>Skip</c>
/// becomes <c>Take</c> - when the counterpart is a twin of the method called: declared on the same
/// type, returning the same type the same way. DEC0024.
/// </summary>
/// <remarks>
/// <para>
/// The pairs are the ones whose two members have the same shape and whose swap changes what the
/// call computes: a quantifier, an extreme, an order, a side of a sequence, a set operation. The
/// pairs that change something else are deliberately absent. <c>First</c> ↔ <c>FirstOrDefault</c>
/// swaps an exception for a <c>null</c> and was near-equivalent six times out of six on
/// <c>Reefact/first-class-errors</c>; <c>Sum</c>, <c>Count</c> and <c>Average</c> swaps change the
/// type; <c>First</c> ↔ <c>Last</c> is equivalent on a single element (#63).
/// </para>
/// <para>
/// The rule names no class. A call is swapped when the counterpart <em>binds</em>, in the same place,
/// to a method of the same containing type with the same return type, returned the same way. That is
/// what reaches <c>System.Linq.ImmutableArrayExtensions.Any</c> - which is where
/// <c>candidates.Any(...)</c> binds in a Roslyn analyzer, and one of the two gaps that motivated the
/// family - as well as <c>Enumerable</c> and <c>Queryable</c>, and leaves alone a user's own
/// <c>Any()</c> whose type declares no <c>All</c>. A user type that declares both with the same
/// shape is swapped: its author made them comparable. Binding the whole swapped call speculatively
/// is what RB-011 does for operators, and the twin test on top is what makes every mutant compile
/// by construction: the call's value keeps its type wherever it is used, and a <c>ref</c> return is
/// never swapped for a value return, which <c>ref bool r = ref x.Any();</c> would refuse.
/// </para>
/// <para>
/// A call through <c>?.</c> - <c>xs?.Any(p)</c> - is not reached in this version: its invocation
/// does not bind on its own, outside the conditional access, and the family is measured before it
/// grows. The whole invocation is the mutated node, so DEC0007's <c>Hit&lt;T&gt;</c> records its
/// coverage as any expression's.
/// </para>
/// </remarks>
internal sealed class LinqMutator : IMutator
{
    private static readonly Dictionary<string, string> Counterparts = BothWays(
        ("Any", "All"),
        ("Min", "Max"),
        ("MinBy", "MaxBy"),
        ("OrderBy", "OrderByDescending"),
        ("ThenBy", "ThenByDescending"),
        ("Order", "OrderDescending"),
        ("Skip", "Take"),
        ("SkipWhile", "TakeWhile"),
        ("SkipLast", "TakeLast"),
        ("Union", "Intersect"),
        ("Concat", "Except"));

    /// <inheritdoc />
    public MutatorName Name { get; } = MutatorName.Create("Linq");

    /// <inheritdoc />
    public IEnumerable<MutationCandidate> Mutate(SyntaxNode node, SemanticModel semanticModel)
    {
        ArgumentNullException.ThrowIfNull(semanticModel);

        if (node is not InvocationExpressionSyntax invocation ||
            invocation.Expression is not MemberAccessExpressionSyntax access ||
            !Counterparts.TryGetValue(access.Name.Identifier.ValueText, out string? counterpart) ||
            semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol called)
        {
            yield break;
        }

        // The name token alone changes, so explicit type arguments - `xs.Any<int>(p)` - and the
        // receiver, the arguments and their lambdas are bound again exactly as written.
        InvocationExpressionSyntax swapped = invocation.ReplaceNode(
            access.Name,
            access.Name.WithIdentifier(
                SyntaxFactory.Identifier(counterpart).WithTriviaFrom(access.Name.Identifier)));

        if (semanticModel
                .GetSpeculativeSymbolInfo(invocation.SpanStart, swapped, SpeculativeBindingOption.BindAsExpression)
                .Symbol is not IMethodSymbol twin ||
            !IsATwinOf(called, twin))
        {
            yield break;
        }

        yield return new MutationCandidate(Name, invocation, swapped.WithTriviaFrom(invocation));
    }

    /// <summary>
    /// True when <paramref name="twin"/> is declared on the type <paramref name="called"/> is, and
    /// returns the same type the same way - by value, by <c>ref</c>, or by <c>ref readonly</c>.
    /// </summary>
    /// <remarks>
    /// The containing types are compared by their definitions, so that a pair declared on a generic
    /// type is one pair whatever the type arguments. The return types are compared constructed, as
    /// the calls bind them: <c>Min</c> and <c>Max</c> over an <c>int[]</c> both return <c>int</c>, and
    /// that is the equality that keeps the call's value fitting where it stood.
    /// </remarks>
    private static bool IsATwinOf(IMethodSymbol called, IMethodSymbol twin) =>
        SymbolEqualityComparer.Default.Equals(
            called.ContainingType.OriginalDefinition, twin.ContainingType.OriginalDefinition) &&
        SymbolEqualityComparer.Default.Equals(called.ReturnType, twin.ReturnType) &&
        called.ReturnsByRef == twin.ReturnsByRef &&
        called.ReturnsByRefReadonly == twin.ReturnsByRefReadonly;

    private static Dictionary<string, string> BothWays(params (string, string)[] pairs)
    {
        Dictionary<string, string> map = new(StringComparer.Ordinal);

        foreach ((string one, string other) in pairs)
        {
            map[one] = other;
            map[other] = one;
        }

        return map;
    }
}
