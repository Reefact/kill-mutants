using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KillMutants.Mutations;

/// <summary>Decides whether mutating a syntax node is worth doing, and whether it is possible.</summary>
internal static class MutationSite
{
    /// <summary>
    /// True when a change here could actually change what a compiled consumer does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// C# evaluates some expressions at compile time and copies the <em>result</em> into every call
    /// site: <c>const</c> values, default parameter values, attribute arguments and enum members.
    /// Mutating one of those changes the assembly under test but not the already-compiled test
    /// assembly that reads it, so the tests cannot possibly notice.
    /// </para>
    /// <para>
    /// Verified: mutating <c>const Limit = 18</c> to <c>99</c> and swapping the assembly left the
    /// consumer still reading <c>18</c>. Such a mutant is guaranteed to survive no matter how good
    /// the tests are, so generating it would manufacture a false gap and depress the score for a
    /// reason the user could never act on. These sites are skipped rather than reported.
    /// </para>
    /// </remarks>
    public static bool IsObservable(SyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        foreach (SyntaxNode ancestor in node.AncestorsAndSelf())
        {
            switch (ancestor)
            {
                // [Obsolete("text")] - baked into the caller's metadata.
                case AttributeArgumentSyntax:

                // enum Level { High = 1 + 2 } - members are compile-time constants.
                case EnumMemberDeclarationSyntax:

                // void M(int limit = 8 * 2) - the default is copied into every call site.
                case ParameterSyntax:
                    return false;

                case FieldDeclarationSyntax field when IsConstant(field.Modifiers):
                case LocalDeclarationStatementSyntax local when IsConstant(local.Modifiers):
                    return false;

                // Stop at the first member: nothing above it can make an expression constant.
                case MemberDeclarationSyntax:
                    return true;

                default:
                    continue;
            }
        }

        return true;
    }

    /// <summary>
    /// True when the expression declares a variable, which makes replacing it unsafe.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A pattern or <c>out</c> variable is definitely assigned only <em>conditionally</em> -
    /// "assigned when this expression is false" - and every mutation this tool makes to such an
    /// expression changes when its parts are evaluated. Turning the <c>||</c> into an <c>&amp;&amp;</c>
    /// in <c>node is not T x || !map.TryGetValue(x.Kind(), out var found)</c> leaves both <c>x</c>
    /// and <c>found</c> unassigned at every later use; swapping the branches of
    /// <c>d.TryGetValue(k, out var v) ? v : 0</c> moves <c>v</c> into the branch where it was never
    /// assigned. Neither compiles.
    /// </para>
    /// <para>
    /// Found by running KillMutants on its own source, where this guard-clause shape is everywhere:
    /// sixteen mutants that could only ever be reported as compile errors, and — because the
    /// coverage probe erases the same conditional state — an instrumented build that failed outright
    /// before a single mutant was tested. See <c>docs/robustness-backlog-en.md</c>, entry RB-016.
    /// </para>
    /// <para>
    /// The rule is deliberately blunt: any declaration anywhere beneath the node, even one whose
    /// scope could not escape it. A mutant that cannot compile teaches nobody anything, so refusing
    /// too much costs only the rare mutation of a declaration nothing reads afterwards.
    /// </para>
    /// </remarks>
    public static bool DeclaresAVariable(SyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.DescendantNodesAndSelf().Any(inner => inner is SingleVariableDesignationSyntax);
    }

    /// <summary>
    /// True when the developer has marked this code as not measured, with
    /// <c>[ExcludeFromCodeCoverage]</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The attribute is a statement about intent: this code is not part of what the tests are
    /// expected to cover. Generated adapters, plumbing and <c>Main</c> methods carry it precisely so
    /// that coverage tools stop reporting them. Mutating it produces survivors the developer has
    /// already said they do not want counted, and now that uncovered and surviving mutants both weigh
    /// on the score, those findings do not merely clutter the report - they move the number.
    /// </para>
    /// <para>
    /// Resolved through the semantic model rather than by matching the attribute's name, so a
    /// same-named attribute of the user's own does not silence a whole class by accident. Stryker.NET
    /// applies the same rule, in <c>ExcludeFromCodeCoverageFilter</c>.
    /// </para>
    /// </remarks>
    public static bool IsExcludedFromCoverage(SyntaxNode node, SemanticModel semanticModel)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(semanticModel);

        foreach (SyntaxNode ancestor in node.AncestorsAndSelf())
        {
            SyntaxList<AttributeListSyntax> attributes = ancestor switch
            {
                MemberDeclarationSyntax member => member.AttributeLists,
                AccessorDeclarationSyntax accessor => accessor.AttributeLists,
                LocalFunctionStatementSyntax local => local.AttributeLists,
                _ => default,
            };

            if (attributes.Count > 0 && attributes.Any(list => Excludes(list, semanticModel)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Excludes(AttributeListSyntax list, SemanticModel semanticModel) =>
        list.Attributes.Any(attribute =>
            semanticModel.GetSymbolInfo(attribute).Symbol?.ContainingType?.ToDisplayString() ==
            "System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute");

    private static bool IsConstant(SyntaxTokenList modifiers) =>
        modifiers.Any(SyntaxKind.ConstKeyword);
}
