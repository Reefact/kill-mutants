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
    /// <para>
    /// One narrow exception lives beside this rule rather than inside it: a constant the project
    /// under test reads itself, offered only to a family that asks for it - see
    /// <see cref="IsAConstantItsProjectReads"/>.
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
    /// True when the node belongs to a <c>const</c> - a field or a local - that the project it is
    /// declared in reads somewhere a change could be seen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="IsObservable"/> refuses a constant because the test assembly keeps the value it was
    /// compiled against. That is the whole truth only for a constant nobody else reads: the project
    /// under test is recompiled with every mutant, so <c>if (count &lt; MinimumNouns)</c> in that
    /// project reads the mutated value, and a test can notice. A constant only the tests read is
    /// still left alone, because there the mutant survives whatever the tests do.
    /// </para>
    /// <para>
    /// A read counts only where <see cref="IsObservable"/> would allow a mutation, so a constant
    /// read only by another constant, an attribute, a parameter's default or an enum member does not
    /// count: those are copied onward in turn. Following such a chain would find more, and costs a
    /// walk per link for a shape that is rare. DEC0019.
    /// </para>
    /// </remarks>
    /// <param name="node">The node a rule proposed to mutate.</param>
    /// <param name="semanticModel">The semantic model of the node's tree.</param>
    /// <param name="identifiers">
    /// Every identifier of the compilation by name - see <see cref="IdentifiersOf"/> - built once
    /// per compilation by the caller, so that each constant binds only the names that match its own
    /// rather than walking every file again.
    /// </param>
    public static bool IsAConstantItsProjectReads(
        SyntaxNode node,
        SemanticModel semanticModel,
        ILookup<string, IdentifierNameSyntax> identifiers)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(semanticModel);
        ArgumentNullException.ThrowIfNull(identifiers);

        // A constant's initializer holds no lambda and no member, so the nearest declarator is the
        // constant's own whenever there is a constant at all.
        VariableDeclaratorSyntax? declarator = node.AncestorsAndSelf()
            .OfType<VariableDeclaratorSyntax>()
            .FirstOrDefault();

        bool isConstant = declarator?.Parent?.Parent switch
        {
            FieldDeclarationSyntax field => IsConstant(field.Modifiers),
            LocalDeclarationStatementSyntax local => local.IsConst,
            _ => false,
        };

        if (!isConstant || semanticModel.GetDeclaredSymbol(declarator!) is not { } constant)
        {
            return false;
        }

        foreach (IGrouping<SyntaxTree, IdentifierNameSyntax> inTree in identifiers[constant.Name]
                     .Where(IsObservable)
                     .GroupBy(identifier => identifier.SyntaxTree))
        {
            SemanticModel model = inTree.Key == semanticModel.SyntaxTree
                ? semanticModel
                : semanticModel.Compilation.GetSemanticModel(inTree.Key);

            if (inTree.Any(identifier =>
                    !IsInsideNameof(identifier, model) &&
                    SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(identifier).Symbol, constant)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True inside <c>nameof(...)</c>, which reads a name and never a value: <c>nameof(Limit)</c>
    /// is <c>"Limit"</c> whatever <c>Limit</c> holds.
    /// </summary>
    /// <remarks>
    /// Asked of the compiler as well as the syntax, since a method that happens to be called
    /// <c>nameof</c> binds to a symbol and the operator does not.
    /// </remarks>
    private static bool IsInsideNameof(SyntaxNode node, SemanticModel semanticModel) =>
        node.Ancestors()
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation => invocation.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" } &&
                               semanticModel.GetSymbolInfo(invocation).Symbol is null);

    /// <summary>
    /// The largest constant expression <paramref name="site"/> is part of: itself, or what encloses
    /// it for as long as that is still a constant.
    /// </summary>
    /// <remarks>
    /// What the compiler checks against a target type is the whole constant: in
    /// <c>byte b = 200 + 55</c> it is <c>255</c> that must fit a <c>byte</c>, not <c>200</c>.
    /// </remarks>
    public static ExpressionSyntax OutermostConstant(ExpressionSyntax site, SemanticModel semanticModel)
    {
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(semanticModel);

        ExpressionSyntax constant = site;

        while (constant.Parent is ExpressionSyntax enclosing &&
               semanticModel.GetConstantValue(enclosing).HasValue)
        {
            constant = enclosing;
        }

        return constant;
    }

    /// <summary>
    /// True when the expression reaches the type it is used as only because it is a constant.
    /// </summary>
    /// <remarks>
    /// C# converts an <c>int</c> constant to a <c>byte</c>, a <c>uint</c> or a <c>sbyte?</c> when
    /// its value fits, and an <c>int</c> that is not a constant to none of them. So
    /// <c>byte b = 100</c> compiles while <c>byte b = Hit(1, 100)</c> fails with CS0266, and
    /// <c>byte b = 101 + 155</c> fails with CS0031 although each literal fits on its own. Asked of
    /// the types alone, which is the conversion a value that is no longer a constant would get.
    /// </remarks>
    public static bool ConvertsOnlyAsAConstant(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(semanticModel);

        TypeInfo info = semanticModel.GetTypeInfo(expression);

        return info.Type is { TypeKind: not TypeKind.Error } type &&
               info.ConvertedType is { TypeKind: not TypeKind.Error } target &&
               !semanticModel.Compilation.ClassifyCommonConversion(type, target).IsImplicit;
    }

    /// <summary>Every identifier in <paramref name="compilation"/>, by the name it spells.</summary>
    /// <remarks>
    /// Generated sources included: they are compiled into the project, so a read there is a read
    /// the mutant reaches.
    /// </remarks>
    public static ILookup<string, IdentifierNameSyntax> IdentifiersOf(Compilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        return compilation.SyntaxTrees
            .SelectMany(tree => tree.GetRoot().DescendantNodes().OfType<IdentifierNameSyntax>())
            .ToLookup(identifier => identifier.Identifier.ValueText, StringComparer.Ordinal);
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
    /// The rule is deliberately blunt: any declaration anywhere beneath the node, even one nothing
    /// reads afterwards. A mutant that cannot compile teaches nobody anything, so refusing too much
    /// costs only that rare mutation. It stops at one boundary, a lambda, an anonymous method or a
    /// local function strictly beneath the node: a variable declared inside one is scoped to it, so
    /// nothing done to the expression around it - a replacement, the probe - changes when its own
    /// parts are evaluated. Without the boundary,
    /// <c>names.OrderByDescending(m => d.TryGetValue(m, out var v) ? Weight(v) : 0)</c> had no
    /// mutant from any family, over a declaration that could not reach the call - found on
    /// <c>Reefact/enum-member-name-binding</c> (#78). The ternary inside that lambda is refused as
    /// before: the declaration is in its own condition, with no boundary between.
    /// </para>
    /// </remarks>
    public static bool DeclaresAVariable(SyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node
            .DescendantNodesAndSelf(inner => inner == node || !IsAFunctionBoundary(inner))
            .Any(inner => inner is SingleVariableDesignationSyntax);
    }

    /// <summary>
    /// True for a scope of its own, which no declaration or assignment inside it reaches past.
    /// </summary>
    private static bool IsAFunctionBoundary(SyntaxNode node) =>
        node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax;

    /// <summary>
    /// True when the expression assigns a variable on some of its paths only, which makes replacing
    /// it unsafe for the same reason as a declaration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// RB-016's rule looks for a declaration, and a variable declared on a line of its own escapes
    /// it: <c>int y; if (s != null &amp;&amp; int.TryParse(s, out y)) { return y; }</c> declares
    /// nothing in the condition, yet <c>y</c> is assigned only when the left operand is true,
    /// because otherwise <c>&amp;&amp;</c> skips its right operand. The compiler therefore knows
    /// <c>y</c> is assigned whenever the whole condition is true, and the <c>return y</c> relies on
    /// it. Turning the <c>&amp;&amp;</c> into an <c>||</c>, or negating the condition, reads
    /// <c>y</c> where it may be unassigned; wrapping the condition in the probe loses what the
    /// compiler knew about the true case the same way, the instrumented build fails with CS0165,
    /// and the run stops before a single mutant is tested - RB-030.
    /// </para>
    /// <para>
    /// Only the parts C# may skip count: the right operand of <c>&amp;&amp;</c>, <c>||</c> or
    /// <c>??</c>, a branch of a ternary, what follows a <c>?.</c>, and a <c>switch</c> arm. An
    /// <c>out</c> in a left operand runs every time, so <c>int.TryParse(s, out y) &amp;&amp; a</c>
    /// is still mutated, and so is a comparison that holds an assignment without skipping it.
    /// A nested function is the same boundary as for a declaration: an assignment inside a lambda
    /// never makes a variable definitely assigned outside it, so it does not count against the
    /// expression around the lambda (#78).
    /// </para>
    /// </remarks>
    public static bool AssignsOnSomePathsOnly(SyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node
            .DescendantNodesAndSelf(inner => inner == node || !IsAFunctionBoundary(inner))
            .Any(inner => IsAnAssignment(inner) && MayBeSkipped(inner, node));
    }

    private static bool IsAnAssignment(SyntaxNode node) =>
        node is AssignmentExpressionSyntax ||
        (node is ArgumentSyntax argument && argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword));

    /// <summary>
    /// True when, between <paramref name="assignment"/> and <paramref name="within"/>, it sits in a
    /// part of an expression C# evaluates only on some paths.
    /// </summary>
    private static bool MayBeSkipped(SyntaxNode assignment, SyntaxNode within)
    {
        for (SyntaxNode current = assignment; current != within && current.Parent is not null; current = current.Parent)
        {
            bool skippable = current.Parent switch
            {
                BinaryExpressionSyntax binary => binary.Right == current &&
                                                 (binary.IsKind(SyntaxKind.LogicalAndExpression) ||
                                                  binary.IsKind(SyntaxKind.LogicalOrExpression) ||
                                                  binary.IsKind(SyntaxKind.CoalesceExpression)),
                ConditionalExpressionSyntax ternary => ternary.Condition != current,
                ConditionalAccessExpressionSyntax access => access.WhenNotNull == current,
                SwitchExpressionArmSyntax => true,
                _ => false,
            };

            if (skippable)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when the expression is part of a lambda the compiler turns into an expression tree.
    /// </summary>
    /// <remarks>
    /// An expression tree accepts no pattern at all (CS8122), so a family whose mutant writes one
    /// asks first: <c>Pattern</c> negates a type test around its operator there (review of #38),
    /// and <c>NullCoalescing</c> does not force a fallback there, since its forced form tests the
    /// left for <c>null</c> with a pattern (review of #81).
    /// </remarks>
    public static bool IsInsideAnExpressionTree(SyntaxNode node, SemanticModel semanticModel)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(semanticModel);

        return node.Ancestors().OfType<AnonymousFunctionExpressionSyntax>().Any(lambda =>
            semanticModel.GetTypeInfo(lambda).ConvertedType is INamedTypeSymbol type &&
            IsAnExpressionTreeType(type));
    }

    private static bool IsAnExpressionTreeType(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString() == "System.Linq.Expressions.LambdaExpression") { return true; }
        }

        return false;
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
