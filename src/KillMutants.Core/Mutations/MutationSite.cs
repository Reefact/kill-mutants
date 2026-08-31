using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KillMutants.Mutations;

/// <summary>Decides whether mutating a syntax node could ever be observed.</summary>
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

    private static bool IsConstant(SyntaxTokenList modifiers) =>
        modifiers.Any(SyntaxKind.ConstKeyword);
}
