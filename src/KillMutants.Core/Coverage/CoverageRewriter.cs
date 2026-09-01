using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KillMutants.Coverage;

/// <summary>Wraps mutation sites in calls that record having been reached.</summary>
internal static class CoverageRewriter
{
    /// <summary>Rewrites <paramref name="root"/> so each named site records its identifier.</summary>
    /// <remarks>
    /// The recorder returns its argument, so wrapping cannot change what an expression evaluates to
    /// or when. That is why this instrumentation needs none of the machinery a mutation switch would:
    /// there is no branch to place, so no context in which the placement is illegal, and therefore no
    /// compile-and-roll-back loop. What it can still change is whether the result <em>parses</em> the
    /// same way - see <see cref="Record"/>.
    /// </remarks>
    public static SyntaxNode Instrument(SyntaxNode root, IReadOnlyDictionary<SyntaxNode, int> identifiers)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(identifiers);

        // The callback receives the rewritten node as well as the original, so nested mutation
        // sites - a comparison inside a logical operator, say - each keep their own recorder.
        return root.ReplaceNodes(
            identifiers.Keys,
            (original, rewritten) => Record(rewritten, identifiers[original], InsideInterpolation(original)));
    }

    /// <summary>
    /// True when the site sits in an interpolation hole, where a bare <c>global::</c> is misread.
    /// </summary>
    /// <remarks>
    /// Inside an interpolated string a top-level colon ends the expression and begins a format
    /// specifier, so <c>$"{global::KillMutantsGenerated.CoverageProbe.Hit(1, a + b)}"</c> parses as
    /// the expression <c>global</c> with the format <c>:KillMutantsGenerated…</c>, and the
    /// instrumented build fails with <c>CS0103: The name 'global' does not exist in the current
    /// context</c>. Parentheses put the colon inside brackets, where the interpolation grammar leaves
    /// it alone.
    /// </remarks>
    private static bool InsideInterpolation(SyntaxNode site) =>
        site.Ancestors().Any(ancestor => ancestor is InterpolationSyntax);

    /// <summary>Wraps one expression in a call that records having reached it.</summary>
    /// <remarks>
    /// <para>
    /// Both forms of this exist because each breaks where the other is required. The bare call is
    /// misread inside an interpolation hole; the parenthesised one is illegal as a statement, since
    /// <c>(M());</c> is a parenthesised expression rather than a call and C# accepts only assignments,
    /// calls, increments and object creations there - <c>CS0201</c>, which is what
    /// <c>total += age;</c> turned into when the parentheses were applied everywhere.
    /// </para>
    /// <para>
    /// The <c>global::</c> qualification is kept in both: without it, a user namespace called
    /// <c>KillMutantsGenerated</c> would silently capture the call.
    /// </para>
    /// </remarks>
    private static ExpressionSyntax Record(SyntaxNode expression, int identifier, bool parenthesise)
    {
        string call =
            $"{CoverageProbe.HitMethod}({identifier.ToString(CultureInfo.InvariantCulture)}, " +
            $"{expression.ToFullString()})";

        return SyntaxFactory
            .ParseExpression(parenthesise ? $"({call})" : call)
            .WithTriviaFrom(expression);
    }
}
