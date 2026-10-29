using KillMutants.Mutations;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KillMutants.Coverage;

/// <summary>
/// The distinct places in the source that mutants occupy, and which mutant stands for each.
/// </summary>
/// <remarks>
/// <para>
/// Several mutants share one expression: <c>age &gt;= 18</c> yields both a boundary shift and a
/// negation from the same node. Coverage is a property of the <em>site</em>, not of the mutant - a
/// test that reaches the expression reaches all of them - so the instrumented build carries one
/// recorder per site, and every mutant there inherits its answer. Instrumenting per mutant would
/// nest recorders inside each other and measure the same thing several times.
/// </para>
/// <para>
/// Not every site can carry a recorder. See <see cref="CanCarryARecorder"/>: a site whose value the
/// probe cannot accept as a type argument is left out, and the mutants there are tested against the
/// whole suite instead of against a measured subset.
/// </para>
/// </remarks>
internal sealed class MutationSites
{
    private MutationSites(
        IReadOnlyDictionary<SyntaxNode, int> identifierByNode,
        IReadOnlyDictionary<MutantId, MutantId> representativeOf,
        IReadOnlySet<MutantId> unmeasurable)
    {
        IdentifierByNode = identifierByNode;
        RepresentativeOf = representativeOf;
        Unmeasurable = unmeasurable;
    }

    /// <summary>The recorder identifier to compile in at each distinct node.</summary>
    public IReadOnlyDictionary<SyntaxNode, int> IdentifierByNode { get; }

    /// <summary>For each mutant, the mutant whose identifier stands for its site.</summary>
    public IReadOnlyDictionary<MutantId, MutantId> RepresentativeOf { get; }

    /// <summary>The representatives of the sites no recorder could be placed at.</summary>
    public IReadOnlySet<MutantId> Unmeasurable { get; }

    /// <summary>Groups mutants by the expression they replace.</summary>
    /// <param name="mutants">The mutants of one project.</param>
    /// <param name="compilation">
    /// The compilation those mutants came from, used to ask what each site's expression is worth -
    /// see <see cref="CanCarryARecorder"/>.
    /// </param>
    public static MutationSites From(IEnumerable<Mutant> mutants, Compilation compilation)
    {
        ArgumentNullException.ThrowIfNull(mutants);
        ArgumentNullException.ThrowIfNull(compilation);

        Dictionary<SyntaxNode, int> identifierByNode = [];
        Dictionary<MutantId, MutantId> representativeOf = [];
        HashSet<MutantId> unmeasurable = [];

        foreach (IGrouping<SyntaxNode, Mutant> atNode in mutants.GroupBy(mutant => mutant.OriginalNode))
        {
            MutantId representative = atNode.First().Id;

            if (CanCarryARecorder(atNode.Key, compilation.GetSemanticModel(atNode.Key.SyntaxTree)))
            {
                identifierByNode[atNode.Key] = representative.Value;
            }
            else
            {
                unmeasurable.Add(representative);
            }

            foreach (Mutant mutant in atNode)
            {
                representativeOf[mutant.Id] = representative;
            }
        }

        return new MutationSites(identifierByNode, representativeOf, unmeasurable);
    }

    /// <summary>
    /// True when the probe can accept this site's value as its type argument.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The recorder is <c>T Hit&lt;T&gt;(int id, T value)</c>, and C# does not let every type be a
    /// <c>T</c>. Verified against the .NET 10 SDK: wrapping an expression of type
    /// <c>Span&lt;int&gt;</c> fails with <c>CS9244 - the type 'Span&lt;int&gt;' may not be a ref
    /// struct [...] in order to use it as parameter 'T'</c>. A conditional expression is a mutation
    /// site and may well have exactly that type, so the case is reachable in ordinary code.
    /// </para>
    /// <para>
    /// The obvious repair - <c>where T : allows ref struct</c> - is not available: the probe is
    /// compiled into the <em>user's</em> project, whose language version we do not control, and that
    /// constraint needs C# 13. Leaving the site uninstrumented costs its mutants a full-suite run,
    /// which is slower but never wrong; getting it wrong costs a build that does not compile at all
    /// and a run that never starts.
    /// </para>
    /// <para>
    /// Pointers and <c>void</c> are refused for the same reason and by the same rule: neither can
    /// be a type argument either.
    /// </para>
    /// <para>
    /// So is any expression a pattern or a <c>case</c> label is made of. Those must be compile-time
    /// constants, and a call is not one: instrumenting <c>s is "abc"</c> fails with
    /// <c>CS9135 - a constant value of type 'string' is expected</c>. Stryker.NET has the same rule
    /// (<c>ConstantPatternSyntaxOrchestrator</c> blocks injection there), and finding it in their
    /// source is what prompted the measurement. The <em>mutation</em> is fine and stays: <c>s is ""</c>
    /// compiles and changes what matches. A <c>when</c> clause is not part of the pattern and is
    /// instrumented normally. A pattern that is itself a site - the <c>Pattern</c> family mutates
    /// <c>&lt; 0 or &gt; 100</c> whole - is not an expression at all, and has nothing a call could
    /// wrap.
    /// </para>
    /// <para>
    /// So is an expression with no natural type. <c>int? x = flag ? a : null</c> only acquires its
    /// type from what it is assigned to, and handing it to a generic method leaves nothing to infer
    /// <c>T</c> from: <c>CS0411</c>. The mutation itself is perfectly valid - the same expression is
    /// mutable and the <c>Conditional</c> family relies on exactly this property - which is why the
    /// two rules are separate. Found by instrumenting the mutator corpus.
    /// </para>
    /// </remarks>
    private static bool CanCarryARecorder(SyntaxNode node, SemanticModel semanticModel)
    {
        if (node.AncestorsAndSelf().Any(ancestor => ancestor is PatternSyntax or SwitchLabelSyntax))
        {
            return false;
        }

        if (IsAConstantTheCompilerReads(node, semanticModel) ||
            IsInAConstantDeclaration(node) ||
            LosesItsConstantConversion(node, semanticModel))
        {
            return false;
        }

        TypeInfo info = semanticModel.GetTypeInfo(node);

        return info.Type is not null && Accepts(info.Type) && Accepts(info.ConvertedType);

        static bool Accepts(ITypeSymbol? type) =>
            type is null ||
            (!type.IsRefLikeType &&
             type.TypeKind != TypeKind.Pointer &&
             type.SpecialType != SpecialType.System_Void);
    }

    /// <summary>
    /// True when the site is a constant, or part of one, whose value reaches the type it is used as
    /// only because it is a constant.
    /// </summary>
    /// <remarks>
    /// <c>byte limit = 100</c> compiles, and <c>byte limit = Hit(1, 100)</c> does not: the probe
    /// returns an <c>int</c> that is no longer a constant, and no implicit conversion takes an
    /// <c>int</c> to a <c>byte</c> (CS0266). The same holds for a <c>uint</c>, an <c>sbyte?</c>,
    /// a <c>byte</c> parameter, and a literal inside <c>-5</c> or <c>200 + 55</c>, where it is the
    /// enclosing constant that needed the conversion. <c>NumericLiteral</c> made it reachable by
    /// mutating initializers (DEC0019); <c>Arithmetic</c> could already reach a constant sum. The
    /// mutants there are kept and tested against the whole suite.
    /// </remarks>
    private static bool LosesItsConstantConversion(SyntaxNode node, SemanticModel semanticModel) =>
        node is ExpressionSyntax site &&
        semanticModel.GetConstantValue(site).HasValue &&
        MutationSite.ConvertsOnlyAsAConstant(MutationSite.OutermostConstant(site, semanticModel), semanticModel);

    /// <summary>
    /// True when the site is part of the value a <c>const</c> is declared with.
    /// </summary>
    /// <remarks>
    /// A constant's initializer must be a constant, and the probe is a call:
    /// <c>const int Limit = Hit(1, 100)</c> fails with CS0133 and the instrumented build with it.
    /// Only <c>NumericLiteral</c> reaches such a site (DEC0019). Nothing would be measured there
    /// anyway - a constant is never evaluated at run time - so its mutants are tested against the
    /// whole suite, like those of any site without a recorder.
    /// </remarks>
    private static bool IsInAConstantDeclaration(SyntaxNode node) =>
        node.Ancestors().Any(ancestor => ancestor switch
        {
            FieldDeclarationSyntax field => field.Modifiers.Any(SyntaxKind.ConstKeyword),
            LocalDeclarationStatementSyntax local => local.IsConst,
            _ => false,
        });

    /// <summary>
    /// True when the site is a constant, or part of one, that the compiler reads to decide which
    /// code is reachable and which variables are assigned.
    /// </summary>
    /// <remarks>
    /// <para>
    /// From <c>while (true)</c> the compiler concludes that the loop ends only by a <c>return</c>
    /// or a <c>break</c>: the method then owes no return after it, and a variable assigned before
    /// every <c>break</c> is assigned after the loop. The probe is a call, and a call is never a
    /// constant, so the wrapped condition settles nothing and the instrumented build fails with
    /// CS0161, CS1643 or CS0165. The run then stops before a single mutant is tested. A loop that
    /// only ends by a <c>return</c> is how a retry or a reader is ordinarily written, so this is
    /// common code - RB-029.
    /// </para>
    /// <para>
    /// The compiler reads a constant there in five places, which C# lists in its rules for
    /// reachability and definite assignment: the condition of a branch, of a loop and of a
    /// ternary, the value a <c>switch</c> statement matches, and an operand of <c>&amp;&amp;</c>
    /// or <c>||</c>, where <c>false || int.TryParse(s, out y)</c> leaves <c>y</c> assigned
    /// whenever it is true. The walk climbs through every enclosing constant first, because what
    /// the compiler reads is the whole of it: <c>false</c> inside <c>while (!false)</c> breaks the
    /// build exactly as <c>true</c> does.
    /// </para>
    /// <para>
    /// The mutants there are kept, and tested against the whole suite like those of any site
    /// without a recorder. Some cannot compile - <c>while (false)</c> in a method that relied on
    /// <c>while (true)</c> - and cost one compilation each, reported as a compile error.
    /// </para>
    /// </remarks>
    private static bool IsAConstantTheCompilerReads(SyntaxNode node, SemanticModel semanticModel)
    {
        if (node is not ExpressionSyntax site || !semanticModel.GetConstantValue(site).HasValue)
        {
            return false;
        }

        ExpressionSyntax constant = site;

        while (constant.Parent is ExpressionSyntax enclosing &&
               semanticModel.GetConstantValue(enclosing).HasValue)
        {
            constant = enclosing;
        }

        return constant.Parent switch
        {
            IfStatementSyntax branch => branch.Condition == constant,
            WhileStatementSyntax loop => loop.Condition == constant,
            DoStatementSyntax loop => loop.Condition == constant,
            ForStatementSyntax loop => loop.Condition == constant,
            ConditionalExpressionSyntax ternary => ternary.Condition == constant,
            SwitchStatementSyntax switched => switched.Expression == constant,
            BinaryExpressionSyntax logical => logical.IsKind(SyntaxKind.LogicalAndExpression) ||
                                              logical.IsKind(SyntaxKind.LogicalOrExpression),
            _ => false,
        };
    }
}
